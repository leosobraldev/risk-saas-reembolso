using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Risk.Domain;

namespace RiskGuard.Modules.Risk.Persistence;

public sealed class RiskDbContext(DbContextOptions<RiskDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "risk";

    public DbSet<RiskEvaluation> Evaluations => Set<RiskEvaluation>();

    public DbSet<RiskFactor> Factors => Set<RiskFactor>();

    public DbSet<CustomerHistory> CustomerHistories => Set<CustomerHistory>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RiskEvaluation>(entity =>
        {
            entity.ToTable("RiskEvaluations");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.DomainEvents);
            entity.Property(e => e.Platform).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ExternalOrderId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.Level).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.HasIndex(e => e.OrderId).IsUnique(); // um pedido, uma avaliacao (idempotencia)
            entity.HasIndex(e => new { e.TenantId, e.CalculatedAt });
            entity.HasIndex(e => new { e.TenantId, e.Level });

            entity.HasMany(e => e.Factors)
                .WithOne()
                .HasForeignKey(f => f.RiskEvaluationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(e => e.Factors).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<RiskFactor>(entity =>
        {
            entity.ToTable("RiskFactors");
            entity.HasKey(f => f.Id);
            entity.Ignore(f => f.DomainEvents);
            entity.Property(f => f.Reason).HasMaxLength(RiskFactor.ReasonMaxLength).IsRequired();
        });

        modelBuilder.Entity<CustomerHistory>(entity =>
        {
            entity.ToTable("CustomerHistories");
            entity.HasKey(h => h.Id);
            entity.Ignore(h => h.DomainEvents);
            entity.Property(h => h.CustomerEmail).HasMaxLength(RiskGuard.Core.Domain.EmailRules.MaxLength).IsRequired();
            entity.HasIndex(h => new { h.TenantId, h.CustomerEmail }).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
