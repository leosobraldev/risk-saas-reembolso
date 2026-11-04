using System.Security.Claims;
using RiskGuard.Infrastructure.Identity;

namespace RiskGuard.UnitTests.Identity;

public class IdentityOptionsValidatorTests
{
    private const string Claim = "https://riskguard.app/tenant_id";

    private static IdentityOptions Valid(
        string audience = "https://api.riskguard.app",
        string domain = "riskguard.us.auth0.com",
        string clientId = "cid",
        string clientSecret = "secret",
        string? authority = null,
        string provider = "Auth0",
        int ttl = 432_000,
        string tenantClaim = Claim) =>
        new()
        {
            Provider = provider,
            Audience = audience,
            Authority = authority,
            TenantClaim = tenantClaim,
            Auth0 = new Auth0Options { Domain = domain, ClientId = clientId, ClientSecret = clientSecret, PasswordTicketTtlSeconds = ttl },
        };

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(IdentityOptions options) => new IdentityOptionsValidator().Validate(null, options);

    [Fact]
    public void A_complete_configuration_is_valid_and_the_authority_is_derived_from_the_domain()
    {
        var options = Valid();

        Validate(options).Succeeded.Should().BeTrue();
        options.EffectiveAuthority.Should().Be("https://riskguard.us.auth0.com/");
    }

    [Fact]
    public void A_custom_https_authority_overrides_the_derived_one()
    {
        var options = Valid(authority: "https://login.riskguard.app/");

        Validate(options).Succeeded.Should().BeTrue();
        options.EffectiveAuthority.Should().Be("https://login.riskguard.app/");
    }

    [Theory]
    [InlineData("", "d.auth0.com", "c", "s")]
    [InlineData("aud", "", "c", "s")]
    [InlineData("aud", "d.auth0.com", "", "s")]
    [InlineData("aud", "d.auth0.com", "c", "")]
    [InlineData("  ", "d.auth0.com", "c", "s")]
    public void Missing_required_values_fail_startup(string audience, string domain, string clientId, string secret)
    {
        var result = Validate(Valid(audience, domain, clientId, secret));

        result.Failed.Should().BeTrue();
        result.Failures.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("https://riskguard.us.auth0.com")]
    [InlineData("riskguard.us.auth0.com/")]
    [InlineData("http://riskguard.us.auth0.com")]
    public void The_domain_must_be_just_a_host_without_scheme_or_path(string domain)
    {
        Validate(Valid(domain: domain)).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://login.riskguard.app/")]
    [InlineData("ftp://login.riskguard.app/")]
    [InlineData("login.riskguard.app")]
    public void The_authority_must_be_https_so_the_signing_keys_cannot_be_intercepted(string authority)
    {
        Validate(Valid(authority: authority)).Failed.Should().BeTrue();
    }

    [Fact]
    public void An_unsupported_provider_fails_startup()
    {
        Validate(Valid(provider: "Okta")).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(2_592_001)]
    public void The_password_ticket_ttl_must_be_reasonable(int ttl)
    {
        Validate(Valid(ttl: ttl)).Failed.Should().BeTrue();
    }

    [Fact]
    public void An_empty_tenant_claim_name_fails_startup()
    {
        Validate(Valid(tenantClaim: " ")).Failed.Should().BeTrue();
    }

    [Fact]
    public void Failures_never_contain_the_client_secret()
    {
        var result = Validate(Valid(audience: string.Empty, clientSecret: "super-secret-value"));

        string.Join(" ", result.Failures!).Should().NotContain("super-secret-value");
    }

    [Fact]
    public void Null_options_are_rejected()
    {
        var act = () => new IdentityOptionsValidator().Validate(null, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

public class TenantClaimMappingTests
{
    private const string Provider = "https://riskguard.app/tenant_id";
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    private static string? Mapped(ClaimsPrincipal principal) => principal.FindFirst("tenant_id")?.Value;

    [Fact]
    public void The_providers_namespaced_claim_becomes_the_internal_tenant_claim()
    {
        var principal = Principal((Provider, TenantId.ToString()), ("sub", "auth0|1"));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        Mapped(principal).Should().Be(TenantId.ToString());
        principal.FindFirst("sub")!.Value.Should().Be("auth0|1"); // o restante nao e tocado
    }

    [Fact]
    public void Without_the_claim_the_user_has_no_tenant()
    {
        var principal = Principal(("sub", "auth0|1"));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        Mapped(principal).Should().BeNull();
    }

    [Fact]
    public void A_raw_internal_claim_in_the_token_is_removed_and_never_trusted()
    {
        var principal = Principal(("tenant_id", TenantId.ToString()));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        Mapped(principal).Should().BeNull();
    }

    [Fact]
    public void The_namespaced_claim_replaces_a_conflicting_raw_one()
    {
        var other = Guid.NewGuid();
        var principal = Principal(("tenant_id", other.ToString()), (Provider, TenantId.ToString()));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        principal.FindAll("tenant_id").Select(c => c.Value).Should().Equal(TenantId.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Malformed_or_empty_values_produce_no_tenant(string value)
    {
        var principal = Principal((Provider, value));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        Mapped(principal).Should().BeNull();
    }

    [Fact]
    public void Conflicting_values_for_the_claim_produce_no_tenant_instead_of_picking_one()
    {
        var principal = Principal((Provider, TenantId.ToString()), (Provider, Guid.NewGuid().ToString()));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        Mapped(principal).Should().BeNull();
    }

    [Fact]
    public void Repeating_the_same_value_is_fine()
    {
        var principal = Principal((Provider, TenantId.ToString()), (Provider, TenantId.ToString()));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        Mapped(principal).Should().Be(TenantId.ToString());
    }

    [Fact]
    public void A_different_claim_name_than_configured_is_ignored()
    {
        var principal = Principal(("https://other.example/tenant_id", TenantId.ToString()));

        IdentityAuthentication.MapTenantClaim(principal, Provider);

        Mapped(principal).Should().BeNull();
    }

    [Fact]
    public void Null_and_non_claims_principals_are_tolerated()
    {
        ((Action)(() => IdentityAuthentication.MapTenantClaim(null, Provider))).Should().NotThrow();
        ((Action)(() => IdentityAuthentication.MapTenantClaim(new ClaimsPrincipal(), Provider))).Should().NotThrow();
    }
}
