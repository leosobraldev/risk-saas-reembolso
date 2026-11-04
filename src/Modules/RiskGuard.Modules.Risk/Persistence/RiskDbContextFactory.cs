using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;

namespace RiskGuard.Modules.Risk.Persistence;

/// <summary>Usado apenas pelo <c>dotnet ef</c> (geracao de migrations); nao acessa banco em tempo de design.</summary>
internal sealed class RiskDbContextFactory : IDesignTimeDbContextFactory<RiskDbContext>
{
    public RiskDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<RiskDbContext>();
        builder.UseRiskGuardNpgsql("Host=localhost;Database=design_time_only;Username=design;Password=design", RiskDbContext.SchemaName);
        return new RiskDbContext(builder.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
