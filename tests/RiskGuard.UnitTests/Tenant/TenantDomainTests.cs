using RiskGuard.Core.Abstractions;
using RiskGuard.Modules.Tenant.Domain;
using DomainTenant = RiskGuard.Modules.Tenant.Domain.Tenant;

namespace RiskGuard.UnitTests.Tenant;

public class TenantDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_email_trims_name_and_starts_active()
    {
        var result = DomainTenant.Create("  Loja X  ", "  Dono@Loja.COM ", Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Loja X");
        result.Value.Email.Should().Be("dono@loja.com");
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(Now);
        result.Value.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string? name)
    {
        var result = DomainTenant.Create(name, "a@b.com", Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_rejects_name_longer_than_the_limit()
    {
        var result = DomainTenant.Create(new string('x', DomainTenant.NameMaxLength + 1), "a@b.com", Now);

        result.Error.Code.Should().Be("tenant.invalid_name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plainaddress")]
    [InlineData("@missing-local.com")]
    [InlineData("Name <a@b.com>")]
    [InlineData("a@b.com, c@d.com")]
    public void Create_rejects_invalid_email(string? email)
    {
        var result = DomainTenant.Create("Loja", email, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("tenant.invalid_email");
    }

    [Fact]
    public void Deactivate_turns_the_tenant_off()
    {
        var tenant = DomainTenant.Create("Loja", "a@b.com", Now).Value;

        tenant.Deactivate();

        tenant.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Default_settings_are_conservative()
    {
        var tenantId = Guid.NewGuid();

        var settings = TenantSettings.CreateDefault(tenantId);

        settings.TenantId.Should().Be(tenantId);
        settings.HighRiskThreshold.Should().Be(80);
        settings.AutoBlockEnabled.Should().BeFalse(); // bloqueio automatico e opt-in
    }

    [Fact]
    public void Default_settings_reject_an_empty_tenant()
    {
        var act = () => TenantSettings.CreateDefault(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
