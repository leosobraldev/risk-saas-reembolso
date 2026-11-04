using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;

namespace RiskGuard.Infrastructure.Persistence;

public static class ModuleDbContextRegistration
{
    /// <summary>
    /// Registra o DbContext do modulo com a conexao restrita da aplicacao (+ tenant na sessao para RLS)
    /// e um migrator que usa a conexao administrativa.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        services.AddScoped<TenantSessionInterceptor>();

        services.AddDbContext<TContext>((sp, builder) =>
            builder
                .UseRiskGuardNpgsql(sp.GetRequiredService<IOptions<PostgresOptions>>().Value.ConnectionString, schema)
                .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));

        services.AddSingleton<IDatabaseMigrator>(sp => new DbContextMigrator<TContext>(sp, schema));

        return services;
    }
}

internal sealed class DbContextMigrator<TContext>(IServiceProvider rootProvider, string schema) : IDatabaseMigrator
    where TContext : ModuleDbContext
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = rootProvider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value.MigrationConnectionString;

        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseRiskGuardNpgsql(admin, schema);

        // Contexto proprio, sem interceptor de tenant: DDL como dono do schema.
        await using var context = ActivatorUtilities.CreateInstance<TContext>(scope.ServiceProvider, builder.Options);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}

public static class DatabaseMigrationExtensions
{
    public static async Task MigrateDatabasesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var migrator in services.GetServices<IDatabaseMigrator>())
        {
            await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
