using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Action.Application.Blockers;
using RiskGuard.Modules.Action.Jobs;
using RiskGuard.Modules.Action.Persistence;

namespace RiskGuard.Modules.Action;

/// <summary>Acoes preventivas (bloqueio/sinalizacao de pedidos de risco alto via API da plataforma). Schema PostgreSQL: <c>action</c>.</summary>
public sealed class ActionModule : IModule
{
    public string Name => "Action";

    public Assembly Assembly => typeof(ActionModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<ActionDbContext>(ActionDbContext.SchemaName);

        // Strategy por plataforma. Os HttpClients nomeados (IHttpClientFactory) sao registrados na Infrastructure.
        services.AddSingleton<IOrderBlocker, ShopifyOrderBlocker>();
        services.AddSingleton<IOrderBlocker, NuvemshopOrderBlocker>();
        services.AddSingleton<IOrderBlockerResolver, OrderBlockerResolver>();

        services.AddScoped<ExecuteOrderActionJob>();
    }
}
