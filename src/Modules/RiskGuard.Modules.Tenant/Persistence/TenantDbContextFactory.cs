using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;

namespace RiskGuard.Modules.Tenant.Persistence;

/// <summary>Usado apenas pelo <c>dotnet ef</c> (geracao de migrations); nao acessa banco em tempo de design.</summary>
internal sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<TenantDbContext>();
        builder.UseRiskGuardNpgsql("Host=localhost;Database=design_time_only;Username=design;Password=design", TenantDbContext.SchemaName);
        return new TenantDbContext(builder.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
