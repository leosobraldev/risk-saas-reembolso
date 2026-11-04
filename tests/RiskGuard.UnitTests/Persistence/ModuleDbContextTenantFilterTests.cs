using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;

namespace RiskGuard.UnitTests.Persistence;

public class ModuleDbContextTenantFilterTests
{
    [Fact]
    public void Query_filter_uses_the_tenant_of_each_context_instance()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Mesmo tipo de contexto (modelo cacheado), tenants diferentes: nao pode haver vazamento.
        using var contextA = CreateContext(tenantA);
        using var contextB = CreateContext(tenantB);

        var sqlA = contextA.Widgets.ToQueryString();
        var sqlB = contextB.Widgets.ToQueryString();

        sqlA.Should().Contain(tenantA.ToString()).And.NotContain(tenantB.ToString());
        sqlB.Should().Contain(tenantB.ToString()).And.NotContain(tenantA.ToString());
    }

    [Fact]
    public void Tables_live_in_the_module_schema()
    {
        using var context = CreateContext(Guid.NewGuid());

        context.Widgets.ToQueryString().Should().Contain("widgets.\"Widgets\"");
    }

    [Fact]
    public void Query_can_opt_out_of_the_filter_only_explicitly()
    {
        var tenant = Guid.NewGuid();
        using var context = CreateContext(tenant);

        context.Widgets.IgnoreQueryFilters().ToQueryString().Should().NotContain(tenant.ToString());
    }

    private static WidgetDbContext CreateContext(Guid tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var builder = new DbContextOptionsBuilder<WidgetDbContext>();
        builder.UseRiskGuardNpgsql("Host=localhost;Database=unused;Username=u;Password=p", "widgets");

        return new WidgetDbContext(builder.Options, tenantContext);
    }

    internal sealed class Widget : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TenantId { get; set; }
    }

    internal sealed class WidgetDbContext(DbContextOptions<WidgetDbContext> options, ITenantContext tenantContext)
        : ModuleDbContext(options, tenantContext)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override string Schema => "widgets";
    }
}
