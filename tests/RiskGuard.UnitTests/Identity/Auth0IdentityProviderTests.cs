using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Infrastructure.Identity.Auth0;
using RiskGuard.UnitTests.Platforms;

namespace RiskGuard.UnitTests.Identity;

/// <summary>Adaptador do Auth0 contra a Management API (respostas simuladas no formato documentado).</summary>
public class Auth0IdentityProviderTests
{
    private const string Domain = "riskguard-tests.us.auth0.com";
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static IOptions<IdentityOptions> Options() => Microsoft.Extensions.Options.Options.Create(new IdentityOptions
    {
        Audience = "https://api.riskguard.test",
        Auth0 = new Auth0Options { Domain = Domain, ClientId = "cid", ClientSecret = "csecret", Connection = "Username-Password-Authentication", PasswordTicketTtlSeconds = 3600 },
    });

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Auth0 falso: token em /oauth/token e rotas da Management API respondidas pelo roteiro.</summary>
    private static (Auth0IdentityProvider Provider, FakeHttpHandler Handler, FakeClock Clock) Create(
        Func<HttpRequestMessage, HttpResponseMessage>? management = null,
        HttpStatusCode tokenStatus = HttpStatusCode.OK,
        int expiresIn = 86_400)
    {
        var handler = new FakeHttpHandler(request =>
            request.RequestUri!.AbsolutePath == "/oauth/token"
                ? FakeHttpHandler.Json(tokenStatus, JsonSerializer.Serialize(new { access_token = "mgmt-token", token_type = "Bearer", expires_in = expiresIn }))
                : (management ?? (_ => FakeHttpHandler.Status(HttpStatusCode.NotFound)))(request));
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var client = new Auth0ManagementClient(HttpTestKit.Factory(handler), Options(), clock);
        return (new Auth0IdentityProvider(client, Options()), handler, clock);
    }

    private static HttpResponseMessage CreatedUser(string id = "auth0|abc123") =>
        FakeHttpHandler.Json(HttpStatusCode.Created, JsonSerializer.Serialize(new { user_id = id, email = "dono@loja.com" }));

    private static HttpResponseMessage Ticket(string url = "https://riskguard-tests.us.auth0.com/lo/reset?ticket=xyz#") =>
        FakeHttpHandler.Json(HttpStatusCode.Created, JsonSerializer.Serialize(new { ticket = url }));

    private static CreateIdentityUserRequest Request() => new("dono@loja.com", "Loja Exemplo", TenantId);

    // ---- criar usuario ----

    [Fact]
    public async Task Creating_a_user_sends_the_documented_body_with_the_tenant_in_app_metadata_and_no_known_password()
    {
        var (provider, handler, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal) ? CreatedUser() : Ticket());

        var result = await provider.CreateUserAsync(Request());

        result.IsSuccess.Should().BeTrue();
        result.Value.ExternalId.Should().Be("auth0|abc123");
        result.Value.PasswordSetupUrl!.Host.Should().Be(Domain);

        var create = handler.Requests.Single(r => r.Uri.AbsolutePath == "/api/v2/users");
        create.Method.Should().Be(HttpMethod.Post);
        create.Headers["Authorization"].Should().Be("Bearer mgmt-token");
        using var body = JsonDocument.Parse(create.Body!);
        var root = body.RootElement;
        root.GetProperty("connection").GetString().Should().Be("Username-Password-Authentication");
        root.GetProperty("email").GetString().Should().Be("dono@loja.com");
        root.GetProperty("app_metadata").GetProperty("tenant_id").GetString().Should().Be(TenantId.ToString());
        root.GetProperty("email_verified").GetBoolean().Should().BeFalse(); // o e-mail NAO e afirmado como verificado
        root.GetProperty("verify_email").GetBoolean().Should().BeFalse();
        root.GetProperty("password").GetString()!.Length.Should().BeGreaterThan(24); // senha aleatoria que ninguem conhece
    }

    [Fact]
    public async Task Each_creation_uses_a_different_random_password()
    {
        var (provider, handler, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal) ? CreatedUser() : Ticket());

        await provider.CreateUserAsync(Request());
        await provider.CreateUserAsync(Request());

        var passwords = handler.Requests.Where(r => r.Uri.AbsolutePath == "/api/v2/users")
            .Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("password").GetString()).ToList();
        passwords.Distinct().Should().HaveCount(2);
        passwords.Should().OnlyContain(p => p!.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit) && p.Any(c => !char.IsLetterOrDigit(c)));
    }

    [Fact]
    public async Task The_password_ticket_requests_the_configured_ttl_and_does_not_assert_the_email_as_verified()
    {
        var (provider, handler, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal) ? CreatedUser() : Ticket());

        await provider.CreateUserAsync(Request());

        var ticket = handler.Requests.Single(r => r.Uri.AbsolutePath == "/api/v2/tickets/password-change");
        using var body = JsonDocument.Parse(ticket.Body!);
        body.RootElement.GetProperty("user_id").GetString().Should().Be("auth0|abc123");
        body.RootElement.GetProperty("ttl_sec").GetInt32().Should().Be(3600);
        body.RootElement.TryGetProperty("mark_email_as_verified", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_failed_ticket_still_succeeds_without_the_link_the_operator_can_resend_from_the_dashboard(HttpStatusCode ticketStatus)
    {
        var (provider, _, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal) ? CreatedUser() : FakeHttpHandler.Status(ticketStatus));

        var result = await provider.CreateUserAsync(Request());

        result.IsSuccess.Should().BeTrue();
        result.Value.PasswordSetupUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("http://insecure.example/ticket")]
    [InlineData("not a url")]
    public async Task A_ticket_that_is_not_an_https_url_is_discarded(string ticket)
    {
        var (provider, _, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal) ? CreatedUser() : Ticket(ticket));

        (await provider.CreateUserAsync(Request())).Value.PasswordSetupUrl.Should().BeNull();
    }

    [Fact]
    public async Task An_existing_user_is_a_conflict()
    {
        var (provider, _, _) = Create(_ => FakeHttpHandler.Status(HttpStatusCode.Conflict));

        var result = await provider.CreateUserAsync(Request());

        result.Error.Should().Be(IdentityErrors.UserAlreadyExists);
    }

    [Fact]
    public async Task A_rejected_request_does_not_echo_the_providers_body()
    {
        var (provider, _, _) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.BadRequest, """{"message":"PasswordStrengthError for dono@loja.com"}"""));

        var result = await provider.CreateUserAsync(Request());

        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().NotContain("dono@loja.com").And.NotContain("PasswordStrength");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Operational_failures_are_reported_as_unavailable(HttpStatusCode status)
    {
        var (provider, _, _) = Create(_ => FakeHttpHandler.Status(status));

        var result = await provider.CreateUserAsync(Request());

        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be("identity.unavailable");
    }

    [Fact]
    public async Task A_response_without_a_valid_user_id_is_not_trusted()
    {
        var (provider, _, _) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.Created, """{"user_id":"../../etc"}"""));

        (await provider.CreateUserAsync(Request())).Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public async Task Network_failures_and_malformed_json_are_unavailable_not_exceptions()
    {
        var (network, _, _) = Create(_ => throw new HttpRequestException("connection refused"));
        var (malformed, _, _) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.Created, "<html>"));

        (await network.CreateUserAsync(Request())).Error.Type.Should().Be(ErrorType.Unavailable);
        (await malformed.CreateUserAsync(Request())).Error.Type.Should().Be(ErrorType.Unavailable);
    }

    // ---- token da Management API ----

    [Fact]
    public async Task The_management_token_uses_client_credentials_with_the_management_audience()
    {
        var (provider, handler, _) = Create(_ => FakeHttpHandler.Status(HttpStatusCode.NoContent));

        await provider.DeleteUserAsync("auth0|abc");

        var token = handler.Requests.Single(r => r.Uri.AbsolutePath == "/oauth/token");
        token.Method.Should().Be(HttpMethod.Post);
        token.Uri.Host.Should().Be(Domain);
        using var body = JsonDocument.Parse(token.Body!);
        body.RootElement.GetProperty("grant_type").GetString().Should().Be("client_credentials");
        body.RootElement.GetProperty("client_id").GetString().Should().Be("cid");
        body.RootElement.GetProperty("client_secret").GetString().Should().Be("csecret");
        body.RootElement.GetProperty("audience").GetString().Should().Be($"https://{Domain}/api/v2/");
    }

    [Fact]
    public async Task The_management_token_is_cached_and_renewed_shortly_before_it_expires()
    {
        var (provider, handler, clock) = Create(_ => FakeHttpHandler.Status(HttpStatusCode.NoContent), expiresIn: 3600);

        await provider.DeleteUserAsync("auth0|a");
        await provider.DeleteUserAsync("auth0|b");
        handler.Requests.Count(r => r.Uri.AbsolutePath == "/oauth/token").Should().Be(1); // reutilizado

        clock.Now = clock.Now.AddMinutes(30);
        await provider.DeleteUserAsync("auth0|c");
        handler.Requests.Count(r => r.Uri.AbsolutePath == "/oauth/token").Should().Be(1);

        clock.Now = clock.Now.AddMinutes(30); // dentro da margem de renovacao (1 min) do vencimento
        await provider.DeleteUserAsync("auth0|d");
        handler.Requests.Count(r => r.Uri.AbsolutePath == "/oauth/token").Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_management_credentials_are_unavailable_and_the_secret_is_never_echoed(HttpStatusCode status)
    {
        var (provider, _, _) = Create(tokenStatus: status);

        var result = await provider.SetBlockedAsync("auth0|abc", blocked: true);

        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Message.Should().NotContain("csecret").And.NotContain("cid");
    }

    // ---- bloquear / remover ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Blocking_patches_the_user_with_the_blocked_flag(bool blocked)
    {
        var (provider, handler, _) = Create(_ => FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await provider.SetBlockedAsync("auth0|abc123", blocked);

        result.IsSuccess.Should().BeTrue();
        var patch = handler.Requests.Single(r => r.Method == HttpMethod.Patch);
        patch.Uri.AbsoluteUri.Should().Be($"https://{Domain}/api/v2/users/auth0%7Cabc123"); // id escapado: o pipe do "auth0|..." vai codificado
        JsonDocument.Parse(patch.Body!).RootElement.GetProperty("blocked").GetBoolean().Should().Be(blocked);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)] // idempotente: ja nao existe
    public async Task Block_and_delete_are_idempotent(HttpStatusCode status)
    {
        var (provider, _, _) = Create(_ => FakeHttpHandler.Status(status));

        (await provider.SetBlockedAsync("auth0|abc", blocked: true)).IsSuccess.Should().BeTrue();
        (await provider.DeleteUserAsync("auth0|abc")).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Provider_errors_on_block_and_delete_are_unavailable(HttpStatusCode status)
    {
        var (provider, _, _) = Create(_ => FakeHttpHandler.Status(status));

        (await provider.SetBlockedAsync("auth0|abc", blocked: true)).Error.Type.Should().Be(ErrorType.Unavailable);
        (await provider.DeleteUserAsync("auth0|abc")).Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../tenants")]
    [InlineData("auth0|abc/../../x")]
    [InlineData("a b")]
    [InlineData("auth0|abc?x=1")]
    public async Task Unsafe_user_ids_never_reach_the_url(string userId)
    {
        var (provider, handler, _) = Create(_ => FakeHttpHandler.Status(HttpStatusCode.NoContent));

        (await provider.SetBlockedAsync(userId, blocked: true)).IsSuccess.Should().BeFalse();
        (await provider.DeleteUserAsync(userId)).IsSuccess.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task All_calls_go_only_to_the_configured_auth0_domain()
    {
        var (provider, handler, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal) ? CreatedUser() : Ticket());

        await provider.CreateUserAsync(Request());
        await provider.SetBlockedAsync("auth0|abc", blocked: true);
        await provider.DeleteUserAsync("auth0|abc");

        handler.Requests.Should().OnlyContain(r => r.Uri.Host == Domain && r.Uri.Scheme == "https");
    }

    [Fact]
    public void The_adapter_identifies_itself()
    {
        Create().Provider.Name.Should().Be("Auth0");
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var (provider, _, _) = Create();

        await ((Func<Task>)(() => provider.CreateUserAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }
}
