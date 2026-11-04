using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Ingestion.Domain;

namespace RiskGuard.Modules.Ingestion.Persistence;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "ingestion";

    public DbSet<RawWebhook> RawWebhooks => Set<RawWebhook>();

    public DbSet<Order> Orders => Set<Order>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RawWebhook>(entity =>
        {
            entity.ToTable("RawWebhooks");
            entity.HasKey(w => w.Id);
            entity.Ignore(w => w.DomainEvents);
            entity.Ignore(w => w.IsPending);
            entity.Property(w => w.Platform).HasMaxLength(50).IsRequired();
            entity.Property(w => w.Headers).HasColumnType("jsonb").IsRequired();
            entity.Property(w => w.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(w => w.LastError).HasMaxLength(RawWebhook.ErrorMaxLength);
            entity.HasIndex(w => new { w.TenantId, w.Status });
            entity.HasIndex(w => new { w.TenantId, w.ReceivedAt });
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(o => o.Id);
            entity.Ignore(o => o.DomainEvents);
            entity.Property(o => o.Platform).HasMaxLength(50).IsRequired();
            entity.Property(o => o.ExternalOrderId).HasMaxLength(200).IsRequired();
            entity.Property(o => o.CustomerEmail).HasMaxLength(RiskGuard.Core.Domain.EmailRules.MaxLength).IsRequired();
            entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
            entity.HasIndex(o => new { o.TenantId, o.Platform, o.ExternalOrderId }).IsUnique(); // idempotencia
            entity.HasIndex(o => new { o.TenantId, o.CustomerEmail }); // historico do cliente (Fase 4)
        });

        base.OnModelCreating(modelBuilder);
    }
}
