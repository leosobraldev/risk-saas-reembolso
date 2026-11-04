using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Risk.Application.Rules;
using RiskGuard.Modules.Risk.Persistence;

namespace RiskGuard.Modules.Risk;

/// <summary>Motor de risco (Pipes and Filters sobre <see cref="IRiskRule"/>). Schema PostgreSQL: <c>risk</c>.</summary>
public sealed class RiskModule : IModule
{
    public string Name => "Risk";

    public Assembly Assembly => typeof(RiskModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<RiskDbContext>(RiskDbContext.SchemaName);

        // Ordem de registro = ordem de execucao. Nova regra = nova linha aqui.
        services.AddSingleton<IRiskRule, SuspiciousEmailRule>();
        services.AddSingleton<IRiskRule, RefundHistoryRule>();
        services.AddSingleton<IRiskRule, HighOrderValueRule>();
        services.AddSingleton<RiskPipeline>();
    }
}
