using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Infrastructure.Tenancy;
using RiskGuard.IntegrationTests.Infrastructure;

namespace RiskGuard.IntegrationTests.Foundation;

/// <summary>Prova, contra um PostgreSQL real, schema por modulo + isolamento por Global Query Filter.</summary>
[Collection(InfrastructureCollection.Name)]
public class PostgresTenantIsolationTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Each_tenant_only_sees_its_own_rows_and_no_tenant_sees_nothing()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var setup = CreateContext(tenantId: null))
        {
            // EnsureCreated seria ignorado: o banco compartilhado ja tem tabelas do Hangfire.
            await setup.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        await using (var writerA = CreateContext(tenantA))
        {
            writerA.Items.Add(new Item { TenantId = tenantA, Name = "a" });
            await writerA.SaveChangesAsync();
        }

        await using (var writerB = CreateContext(tenantB))
        {
            writerB.Items.AddRange(new Item { TenantId = tenantB, Name = "b1" }, new Item { TenantId = tenantB, Name = "b2" });
            await writerB.SaveChangesAsync();
        }

        await using var readerA = CreateContext(tenantA);
        await using var readerB = CreateContext(tenantB);
        await using var anonymous = CreateContext(tenantId: null);

        (await readerA.Items.Select(i => i.Name).ToListAsync()).Should().BeEquivalentTo("a");
        (await readerB.Items.Select(i => i.Name).ToListAsync()).Should().BeEquivalentTo("b1", "b2");
        (await anonymous.Items.CountAsync()).Should().Be(0);

        var schema = await readerA.Database
            .SqlQueryRaw<string>("SELECT table_schema AS \"Value\" FROM information_schema.tables WHERE table_name = 'Items'")
            .ToListAsync();
        schema.Should().ContainSingle().Which.Should().Be("integration_probe");
    }

    private ProbeDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new TenantContext();
        if (tenantId is { } id)
        {
            tenantContext.SetTenant(id);
        }

        var builder = new DbContextOptionsBuilder<ProbeDbContext>();
        builder.UseRiskGuardNpgsql(infrastructure.PostgresConnectionString, "integration_probe");

        return new ProbeDbContext(builder.Options, tenantContext);
    }

    public sealed class Item : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TenantId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options, ITenantContext tenantContext)
        : ModuleDbContext(options, tenantContext)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override string Schema => "integration_probe";
    }
}
