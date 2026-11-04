using System.Net;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.IntegrationTests.Tenant;

namespace RiskGuard.IntegrationTests.Security;

/// <summary>Borda HTTP: headers de defesa, CORS explicito, proxies confiaveis (anti-spoof de IP), HSTS e erros sem vazamento.</summary>
[Collection(InfrastructureCollection.Name)]
public class HttpHardeningTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri Protected = new("/api/v1/tenants/me", UriKind.Relative);

    private static HttpRequestMessage Get(string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        request.Headers.Host = "api.riskguard.test"; // o HSTS do ASP.NET ignora localhost
        return request;
    }

    private static Dictionary<string, string> Settings(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    // ---- headers ----

    [Fact]
    public async Task Api_responses_carry_the_defensive_headers_even_on_errors()
    {
        using var client = infrastructure.Api.CreateClient();

        foreach (var path in new[] { "/api/v1/tenants/me", "/api/v1/nao-existe", "/health/live" })
        {
            using var response = await client.SendAsync(Get(path));

            response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff", path);
            response.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY", path);
            response.Headers.GetValues("Referrer-Policy").Single().Should().Be("no-referrer", path);
            response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'").And.Contain("frame-ancestors 'none'");
            response.Headers.CacheControl!.NoStore.Should().BeTrue(path);
            response.Headers.Contains("Permissions-Policy").Should().BeTrue(path);
        }
    }

    [Fact]
    public async Task The_hangfire_dashboard_keeps_its_own_headers_because_it_needs_inline_scripts()
    {
        using var client = infrastructure.Api.CreateClient();

        using var response = await client.SendAsync(Get("/hangfire"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
    }

    // ---- CORS ----

    [Fact]
    public async Task Cors_is_closed_by_default()
    {
        using var client = infrastructure.Api.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, Protected)
        {
            Headers = { { "Origin", "https://evil.example" }, { "Access-Control-Request-Method", "GET" } },
        });

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Only_configured_origins_pass_preflight_and_credentials_are_never_allowed()
    {
        await using var factory = new ApiFactory(infrastructure, extraSettings: Settings(("Cors:AllowedOrigins:0", "https://app.riskguard.test")));
        using var client = factory.CreateClient();

        using var allowed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, Protected)
        {
            Headers = { { "Origin", "https://app.riskguard.test" }, { "Access-Control-Request-Method", "GET" }, { "Access-Control-Request-Headers", "authorization" } },
        });
        using var denied = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, Protected)
        {
            Headers = { { "Origin", "https://evil.example" }, { "Access-Control-Request-Method", "GET" } },
        });
        using var subdomain = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, Protected)
        {
            Headers = { { "Origin", "https://app.riskguard.test.evil.example" }, { "Access-Control-Request-Method", "GET" } },
        });

        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://app.riskguard.test");
        allowed.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        denied.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        subdomain.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_cors_or_proxy_configuration_prevents_startup()
    {
        await using var wildcard = new ApiFactory(infrastructure, extraSettings: Settings(("Cors:AllowedOrigins:0", "*")));
        await using var badProxy = new ApiFactory(infrastructure, extraSettings: Settings(("Proxy:KnownProxies:0", "not-an-ip")));

        ((Action)(() => wildcard.CreateClient())).Should().Throw<InvalidOperationException>();
        ((Action)(() => badProxy.CreateClient())).Should().Throw<InvalidOperationException>();
    }

    // ---- proxies / IP real ----

    private static async Task<int> Hit(HttpClient client, string remote, string? forwardedFor, int times)
    {
        var limited = 0;
        for (var i = 0; i < times; i++)
        {
            using var request = Get("/api/v1/qualquer", ("X-Test-Remote-Ip", remote));
            if (forwardedFor is not null)
            {
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor.Replace("{i}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }

            using var response = await client.SendAsync(request);
            limited += response.StatusCode == HttpStatusCode.TooManyRequests ? 1 : 0;
        }

        return limited;
    }

    [Fact]
    public async Task Without_trusted_proxies_a_spoofed_x_forwarded_for_cannot_escape_the_rate_limit()
    {
        await using var factory = new ApiFactory(infrastructure, permitLimit: 3);
        using var client = factory.CreateClient();

        var limited = await Hit(client, "10.0.0.1", "203.0.113.{i}", times: 6); // cada requisicao finge ser um IP diferente

        limited.Should().Be(3); // as 3 primeiras passam, as 3 seguintes recebem 429: o header foi ignorado
    }

    [Fact]
    public async Task A_trusted_proxy_gets_each_clients_own_rate_limit_bucket()
    {
        await using var factory = new ApiFactory(infrastructure, permitLimit: 3, extraSettings: Settings(("Proxy:KnownProxies:0", "10.0.0.1")));
        using var client = factory.CreateClient();

        var distinctClients = await Hit(client, "10.0.0.1", "203.0.113.{i}", times: 6);
        var sameClient = await Hit(client, "10.0.0.1", "198.51.100.9", times: 6);

        distinctClients.Should().Be(0); // 6 clientes diferentes: nenhum estoura o proprio limite
        sameClient.Should().Be(3); // o mesmo cliente 6x: so 3 passam
    }

    [Fact]
    public async Task An_untrusted_source_cannot_use_x_forwarded_for_even_when_a_proxy_is_configured()
    {
        await using var factory = new ApiFactory(infrastructure, permitLimit: 3, extraSettings: Settings(("Proxy:KnownProxies:0", "10.0.0.1")));
        using var client = factory.CreateClient();

        var limited = await Hit(client, "10.0.0.99", "203.0.113.{i}", times: 6); // nao e o proxy configurado

        limited.Should().Be(3);
    }

    [Fact]
    public async Task Hsts_is_sent_only_for_https_outside_development()
    {
        await using var production = new ApiFactory(infrastructure, "Production", extraSettings: Settings(("Proxy:KnownProxies:0", "10.0.0.1")));
        await using var development = new ApiFactory(infrastructure, "Development", extraSettings: Settings(("Proxy:KnownProxies:0", "10.0.0.1")));
        using var prodClient = production.CreateClient();
        using var devClient = development.CreateClient();

        using var overHttps = await prodClient.SendAsync(Get("/health/live", ("X-Test-Remote-Ip", "10.0.0.1"), ("X-Forwarded-Proto", "https")));
        using var overHttp = await prodClient.SendAsync(Get("/health/live", ("X-Test-Remote-Ip", "10.0.0.1")));
        using var dev = await devClient.SendAsync(Get("/health/live", ("X-Test-Remote-Ip", "10.0.0.1"), ("X-Forwarded-Proto", "https")));

        overHttps.Headers.GetValues("Strict-Transport-Security").Single().Should().Contain("max-age=").And.Contain("includeSubDomains");
        overHttp.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
        dev.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }

    // ---- erros ----

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2")]
    [InlineData("\"texto\"")]
    public async Task Malformed_bodies_are_client_errors_with_a_generic_message_never_a_500(string body)
    {
        var (_, email) = await TenantTestHarness.RegisterAsync(infrastructure.Api);
        using var client = await TenantTestHarness.AuthenticatedClientAsync(infrastructure.Api, email);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PutAsync(new Uri("/api/v1/tenants/me/settings", UriKind.Relative), content);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        text.Should().Contain("Requisicao invalida.").And.NotContainEquivalentOf("Exception").And.NotContainEquivalentOf("System.Text.Json").And.NotContainEquivalentOf("UpdateSettingsRequest");
    }

    [Fact]
    public async Task An_invalid_enum_in_the_query_is_a_400_not_a_500()
    {
        var (_, email) = await TenantTestHarness.RegisterAsync(infrastructure.Api);
        using var client = await TenantTestHarness.AuthenticatedClientAsync(infrastructure.Api, email);

        using var response = await client.GetAsync(new Uri("/api/v1/risk/evaluations?level=Catastrophic", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unexpected_failure_returns_a_generic_problem_without_leaking_internals()
    {
        await using var factory = new ApiFactory(infrastructure, "Production");
        var (tenantA, emailA) = await TenantTestHarness.RegisterAsync(factory);
        var (tenantB, _) = await TenantTestHarness.RegisterAsync(factory);
        // corrupcao de dados provoca excecao interna (AEAD): o segredo de A e copiado para a linha de B
        await TenantTestHarness.ExecuteAsAdminAsync(
            infrastructure,
            """
            UPDATE tenant."PlatformConnections" b SET "WebhookSecretProtected" = a."WebhookSecretProtected"
            FROM tenant."PlatformConnections" a
            WHERE a."TenantId" = @a AND b."TenantId" = @b AND a."Platform" = 'shopify' AND b."Platform" = 'shopify'
            """,
            ("a", tenantA), ("b", tenantB));
        using var client = factory.CreateClient();

        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantB, "{}", secretOverride: "qualquer");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.Should().NotContainEquivalentOf("Cryptograph").And.NotContainEquivalentOf("Exception")
            .And.NotContain(" at ").And.NotContainEquivalentOf("PlatformConnection").And.NotContain(emailA);
        response.Headers.Contains("Server").Should().BeFalse();
    }
}
