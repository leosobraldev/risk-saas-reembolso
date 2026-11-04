using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;

namespace RiskGuard.Modules.Action.Persistence;

/// <summary>Usado apenas pelo <c>dotnet ef</c> (geracao de migrations); nao acessa banco em tempo de design.</summary>
internal sealed class ActionDbContextFactory : IDesignTimeDbContextFactory<ActionDbContext>
{
    public ActionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ActionDbContext>();
        builder.UseRiskGuardNpgsql("Host=localhost;Database=design_time_only;Username=design;Password=design", ActionDbContext.SchemaName);
        return new ActionDbContext(builder.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
