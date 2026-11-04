using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Action.Domain;

namespace RiskGuard.Modules.Action.Persistence;

public sealed class ActionDbContext(DbContextOptions<ActionDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "action";

    public DbSet<OrderAction> OrderActions => Set<OrderAction>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OrderAction>(entity =>
        {
            entity.ToTable("OrderActions");
            entity.HasKey(a => a.Id);
            entity.Ignore(a => a.DomainEvents);
            entity.Ignore(a => a.IsPending);
            entity.Property(a => a.Platform).HasMaxLength(50).IsRequired();
            entity.Property(a => a.ExternalOrderId).HasMaxLength(200).IsRequired();
            entity.Property(a => a.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(a => a.LastError).HasMaxLength(OrderAction.ErrorMaxLength);
            entity.HasIndex(a => a.EvaluationId).IsUnique(); // idempotencia por avaliacao de risco
            entity.HasIndex(a => new { a.TenantId, a.ExternalOrderId });
            entity.HasIndex(a => new { a.TenantId, a.Status });
        });

        base.OnModelCreating(modelBuilder);
    }
}
