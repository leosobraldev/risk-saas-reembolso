using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Tenant.Application.Settings;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Tenant;

/// <summary>
/// A API nao tem login: ela valida o JWT RS256 do provedor (emissor, audiencia, assinatura, validade) e converte o claim namespaced do
/// lojista no claim interno. Aqui os tokens sao emitidos por uma chave de teste, no mesmo formato do Auth0.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class JwtAuthenticationTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri MeRoute = new("/api/v1/tenants/me", UriKind.Relative);

    private HttpClient Authenticated(string token)
    {
        var client = infrastructure.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpStatusCode> StatusWith(string token)
    {
        using var client = Authenticated(token);
        return (await client.GetAsync(MeRoute)).StatusCode;
    }

    [Fact]
    public async Task Without_a_token_the_protected_endpoint_returns_401()
    {
        using var client = infrastructure.Api.CreateClient();

        (await client.GetAsync(MeRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Garbage_and_wrong_scheme_are_rejected()
    {
        (await StatusWith("this.is.not-a-jwt")).Should().Be(HttpStatusCode.Unauthorized);

        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", TestIdentity.CreateToken(tenantId));
        (await client.GetAsync(MeRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_token_returns_only_the_callers_tenant_and_its_settings()
    {
        var (tenantA, emailA) = await RegisterAsync(infrastructure.Api, name: "Loja A");
        var (tenantB, emailB) = await RegisterAsync(infrastructure.Api, name: "Loja B");
        using var clientA = Authenticated(AccessTokenFor(emailA));
        using var clientB = Authenticated(AccessTokenFor(emailB));

        var a = await clientA.GetFromJsonAsync<TenantBody>(MeRoute);
        var b = await clientB.GetFromJsonAsync<TenantBody>(MeRoute);

        a!.Id.Should().Be(tenantA);
        a.Name.Should().Be("Loja A");
        a.Settings.Should().Be(new SettingsBody(80, false));
        b!.Id.Should().Be(tenantB);
        b.Name.Should().Be("Loja B");
    }

    // ---- assinatura, emissor, audiencia, validade ----

    [Fact]
    public async Task Expired_tokens_are_rejected()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec
        {
            TenantId = tenantId,
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1),
        });

        (await StatusWith(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tokens_signed_with_a_key_that_is_not_the_providers_are_rejected_even_with_the_same_kid()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        var forged = TestIdentity.CreateToken(new TestIdentity.TokenSpec { TenantId = tenantId, Key = TestIdentity.AttackerKey });

        (await StatusWith(forged)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("https://evil.example/", TestIdentity.Audience)]
    [InlineData(TestIdentity.Issuer, "https://outra-api.example")]
    public async Task Tokens_for_another_issuer_or_audience_are_rejected(string issuer, string audience)
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec { TenantId = tenantId, IssuerValue = issuer, AudienceValue = audience });

        (await StatusWith(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unsigned_alg_none_tokens_are_rejected()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        var payload = $$"""
            { "iss": "{{TestIdentity.Issuer}}", "aud": "{{TestIdentity.Audience}}", "sub": "auth0|x", "{{TestIdentity.TenantClaim}}": "{{tenantId}}",
              "exp": {{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}} }
            """;
        var unsigned = new JsonWebTokenHandler().CreateToken(payload); // sem credenciais de assinatura => alg "none"

        (await StatusWith(unsigned)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tokens_signed_with_a_symmetric_algorithm_are_rejected_algorithm_confusion()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIdentity.Issuer,
            Audience = TestIdentity.Audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["sub"] = "auth0|x", [TestIdentity.TenantClaim] = tenantId.ToString() },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(new byte[64]), SecurityAlgorithms.HmacSha256),
        };

        (await StatusWith(new JsonWebTokenHandler().CreateToken(descriptor))).Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- o claim do lojista ----

    [Fact]
    public async Task A_machine_to_machine_token_without_a_tenant_claim_is_forbidden()
    {
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec { TenantId = null, Subject = "client-id@clients" });

        (await StatusWith(token)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_raw_tenant_id_claim_in_the_token_is_never_trusted()
    {
        var (victim, _) = await RegisterAsync(infrastructure.Api);
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec
        {
            TenantId = null,
            ExtraClaims = new Dictionary<string, object> { [TestIdentity.InternalTenantClaim] = victim.ToString() },
        });

        (await StatusWith(token)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_namespaced_claim_wins_over_a_raw_tenant_id_that_tries_to_override_it()
    {
        var (own, ownEmail) = await RegisterAsync(infrastructure.Api, name: "Loja propria");
        var (victim, _) = await RegisterAsync(infrastructure.Api, name: "Loja vitima");
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec
        {
            TenantId = own,
            ExtraClaims = new Dictionary<string, object> { [TestIdentity.InternalTenantClaim] = victim.ToString() },
        });
        using var client = Authenticated(token);

        var me = await client.GetFromJsonAsync<TenantBody>(MeRoute);

        me!.Id.Should().Be(own);
        me.Name.Should().Be("Loja propria");
        ownEmail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_tenant_claim_under_a_different_name_than_configured_is_ignored()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec { TenantId = tenantId, TenantClaimName = "https://other.example/tenant_id" });

        (await StatusWith(token)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public async Task A_malformed_tenant_claim_value_is_forbidden(string value)
    {
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec
        {
            ExtraClaims = new Dictionary<string, object> { [TestIdentity.TenantClaim] = value },
        });

        (await StatusWith(token)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_tenant_claim_with_conflicting_values_is_forbidden()
    {
        var token = TestIdentity.CreateToken(new TestIdentity.TokenSpec
        {
            ExtraClaims = new Dictionary<string, object> { [TestIdentity.TenantClaim] = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() } },
        });

        (await StatusWith(token)).Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- lojista inativo / inexistente ----

    [Fact]
    public async Task A_valid_token_of_a_deactivated_tenant_is_refused_immediately_on_every_endpoint()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api);
        using var client = Authenticated(AccessTokenFor(email));
        (await client.GetAsync(MeRoute)).StatusCode.Should().Be(HttpStatusCode.OK); // aquece o cache

        await using (var scope = infrastructure.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DeactivateTenantCommand(tenantId));
        }

        foreach (var path in new[] { "/api/v1/tenants/me", "/api/v1/actions", "/api/v1/risk/evaluations", "/api/v1/tenants/me/connections" })
        {
            (await client.GetAsync(new Uri(path, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    [Fact]
    public async Task A_valid_token_for_a_tenant_that_does_not_exist_is_forbidden()
    {
        // usuario orfao: existe no provedor, mas o cadastro do lojista nao existe (ex.: compensacao que falhou)
        var token = TestIdentity.CreateToken(Guid.NewGuid());

        (await StatusWith(token)).Should().Be(HttpStatusCode.Forbidden);
    }
}
