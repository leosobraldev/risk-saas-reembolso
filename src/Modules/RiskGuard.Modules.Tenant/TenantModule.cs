using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant;

/// <summary>Cadastro de lojistas, autenticacao e configuracoes do tenant. Schema PostgreSQL: <c>tenant</c>.</summary>
public sealed class TenantModule : IModule
{
    public string Name => "Tenant";

    public Assembly Assembly => typeof(TenantModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<TenantDbContext>(TenantDbContext.SchemaName);
        services.AddScoped<Application.Settings.TenantSnapshotInvalidator>();

        // Retencao da trilha de auditoria: limpeza recorrente.
        services.AddScoped<Application.Audit.PurgeExpiredAuditEventsJob>();
        services.AddHostedService<Application.Audit.AuditRetentionJobRegistrar>();
    }
}
