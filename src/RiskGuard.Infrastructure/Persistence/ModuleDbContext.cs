using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Infrastructure.Persistence;

/// <summary>
/// Base dos DbContexts dos modulos: cada modulo usa um schema proprio no PostgreSQL (RNF02) e
/// todas as entidades <see cref="ITenantEntity"/> recebem o Global Query Filter por <c>TenantId</c> (4.1).
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(ModuleDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly ITenantContext _tenantContext;

    protected ModuleDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>Schema do modulo (ex.: "tenant", "ingestion").</summary>
    protected abstract string Schema { get; }

    // Membro da instancia lido pelo EF a cada query: o modelo e cacheado por tipo de contexto,
    // mas este valor vira parametro da query e acompanha o tenant do contexto corrente.
    // Sem tenant no contexto, nenhuma linha casa (fail-closed).
    private Guid? CurrentTenantId => _tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(ITenantEntity).IsAssignableFrom(e.ClrType) && e.BaseType is null))
        {
            ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }

        base.OnModelCreating(modelBuilder);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
