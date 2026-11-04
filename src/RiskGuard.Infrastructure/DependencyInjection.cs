using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Caching;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Infrastructure.Jobs;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Infrastructure.Security;
using RiskGuard.Infrastructure.Tenancy;
using StackExchange.Redis;

namespace RiskGuard.Infrastructure;

public static class DependencyInjection
{
    public const string HangfireDashboardPath = "/hangfire";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<PostgresOptions>(configuration, PostgresOptions.SectionName);
        services.AddValidatedOptions<RedisOptions>(configuration, RedisOptions.SectionName);
        services.AddValidatedOptions<RateLimitingOptions>(configuration, RateLimitingOptions.SectionName);
        services.AddValidatedOptions<HangfireOptions>(configuration, HangfireOptions.SectionName);
        services.AddValidatedOptions<RetentionOptions>(configuration, RetentionOptions.SectionName);
        services.AddValidatedOptions<PlatformApisOptions>(configuration, PlatformApisOptions.SectionName);
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.TryGetKeyBytes() is not null, "Security:EncryptionKey deve ser base64 de exatamente 32 bytes.")
            .ValidateOnStart();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IActorContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IActorContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddIdentity(configuration);
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddPlatformHttpClients();

        services.AddRedis();
        var serverEnabled = configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()?.ServerEnabled ?? true;
        services.AddHangfireJobs(serverEnabled);

        return services;
    }

    public static IApplicationBuilder UseRiskGuardHangfireDashboard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseHangfireDashboard(HangfireDashboardPath, new DashboardOptions
        {
            Authorization = [new DevelopmentOnlyDashboardAuthorizationFilter()],
            IsReadOnlyFunc = _ => true,
        });
    }

    private static void AddValidatedOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string section)
        where TOptions : class
        => services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

    private static void AddRedis(this IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisOptions = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var configuration = ConfigurationOptions.Parse(redisOptions.ConnectionString);
            configuration.AbortOnConnectFail = false; // a API sobe mesmo com Redis indisponivel; o health check sinaliza
            return ConnectionMultiplexer.Connect(configuration);
        });

        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
    }

    private static void AddHangfireJobs(this IServiceCollection services, bool serverEnabled)
    {
        services.AddSingleton<IBackgroundJobScheduler, HangfireBackgroundJobScheduler>();
        services.AddSingleton<IDatabaseMigrator, HangfireSchemaMigrator>();

        services.AddHangfire((sp, config) =>
        {
            var postgres = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            var hangfire = sp.GetRequiredService<IOptions<HangfireOptions>>().Value;

            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    storage => storage.UseNpgsqlConnection(postgres.HangfireConnectionString),
                    // O schema e instalado pelo passo de migrations (credencial administrativa); em runtime o papel de jobs
                    // so tem permissoes DML e nao cria nada.
                    new PostgreSqlStorageOptions { SchemaName = hangfire.SchemaName, PrepareSchemaIfNecessary = false });

            // RNF04: retry automatico. Substitui apenas o filtro de retry padrao, preservando os demais filtros globais.
            foreach (var existing in GlobalJobFilters.Filters.Where(f => f.Instance is AutomaticRetryAttribute).ToList())
            {
                GlobalJobFilters.Filters.Remove(existing.Instance);
            }

            config.UseFilter(new AutomaticRetryAttribute { Attempts = hangfire.MaxRetryAttempts });
        });

        if (serverEnabled)
        {
            services.AddHangfireServer((sp, server) =>
                server.WorkerCount = sp.GetRequiredService<IOptions<HangfireOptions>>().Value.WorkerCount);
        }
    }

    /// <summary>Um HttpClient nomeado por plataforma (IHttpClientFactory): timeout e User-Agent vindos da configuracao do servidor.</summary>
    private static void AddPlatformHttpClients(this IServiceCollection services)
    {
        foreach (var platform in PlatformIds.All)
        {
            services.AddHttpClient(platform).ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PlatformApisOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            });
        }
    }
}
