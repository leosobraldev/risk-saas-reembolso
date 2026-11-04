using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Tenancy;

namespace RiskGuard.UnitTests.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void New_context_has_no_tenant()
    {
        var context = new TenantContext();

        context.TenantId.Should().BeNull();
        ((ITenantContext)context).HasTenant.Should().BeFalse();
    }

    [Fact]
    public void SetTenant_stores_the_tenant()
    {
        var id = Guid.NewGuid();
        var context = new TenantContext();

        context.SetTenant(id);

        context.TenantId.Should().Be(id);
    }

    [Fact]
    public void SetTenant_rejects_empty_guid()
    {
        var context = new TenantContext();

        var act = () => context.SetTenant(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetTenant_is_idempotent_for_the_same_tenant()
    {
        var id = Guid.NewGuid();
        var context = new TenantContext();
        context.SetTenant(id);

        var act = () => context.SetTenant(id);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_scope_can_never_switch_to_another_tenant()
    {
        var context = new TenantContext();
        context.SetTenant(Guid.NewGuid());

        var act = () => context.SetTenant(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }
}
