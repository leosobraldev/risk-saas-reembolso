using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Tenant.Domain;

namespace RiskGuard.Modules.Tenant.Persistence;

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "tenant";

    public DbSet<Domain.Tenant> Tenants => Set<Domain.Tenant>();

    public DbSet<TenantSettings> Settings => Set<TenantSettings>();

    public DbSet<PlatformConnection> PlatformConnections => Set<PlatformConnection>();

    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Domain.Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(t => t.Id);
            entity.Ignore(t => t.DomainEvents);
            entity.Property(t => t.Name).HasMaxLength(Domain.Tenant.NameMaxLength).IsRequired();
            entity.Property(t => t.Email).HasMaxLength(Domain.Tenant.EmailMaxLength).IsRequired();
            entity.HasIndex(t => t.Email).IsUnique();
        });

        modelBuilder.Entity<TenantSettings>(entity =>
        {
            entity.ToTable("TenantSettings");
            entity.HasKey(s => s.TenantId);
            entity.HasOne<Domain.Tenant>().WithOne().HasForeignKey<TenantSettings>(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlatformConnection>(entity =>
        {
            entity.ToTable("PlatformConnections");
            entity.HasKey(c => c.Id);
            entity.Ignore(c => c.DomainEvents);
            entity.Property(c => c.Platform).HasMaxLength(50).IsRequired();
            entity.Property(c => c.AccountId).HasMaxLength(200).IsRequired();
            entity.Property(c => c.WebhookSecretProtected).HasMaxLength(2000).IsRequired();
            entity.Property(c => c.AccessTokenProtected).HasMaxLength(4000);
            entity.HasIndex(c => new { c.TenantId, c.Platform }).IsUnique();
        });

        modelBuilder.Entity<TenantUser>(entity =>
        {
            entity.ToTable("TenantUsers");
            entity.HasKey(u => u.Id);
            entity.Ignore(u => u.DomainEvents);
            entity.Property(u => u.ExternalUserId).HasMaxLength(128).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(Domain.Tenant.EmailMaxLength).IsRequired();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(u => u.ExternalUserId).IsUnique(); // um usuario do provedor pertence a um unico lojista
            entity.HasIndex(u => u.TenantId);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.DomainEvents);
            entity.Property(e => e.ActorId).HasMaxLength(AuditEvent.ActorMaxLength).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(AuditEvent.ActionMaxLength).IsRequired();
            entity.Property(e => e.Target).HasMaxLength(AuditEvent.TargetMaxLength);
            entity.Property(e => e.Details).HasMaxLength(AuditEvent.DetailsMaxLength);
            entity.HasIndex(e => new { e.TenantId, e.OccurredAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
