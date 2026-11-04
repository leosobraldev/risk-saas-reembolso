using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;

namespace RiskGuard.Modules.Ingestion.Persistence;

/// <summary>Usado apenas pelo <c>dotnet ef</c> (geracao de migrations); nao acessa banco em tempo de design.</summary>
internal sealed class IngestionDbContextFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IngestionDbContext>();
        builder.UseRiskGuardNpgsql("Host=localhost;Database=design_time_only;Username=design;Password=design", IngestionDbContext.SchemaName);
        return new IngestionDbContext(builder.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
