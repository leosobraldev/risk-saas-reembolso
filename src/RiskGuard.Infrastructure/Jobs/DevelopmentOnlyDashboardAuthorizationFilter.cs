using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RiskGuard.Infrastructure.Jobs;

/// <summary>
/// Painel do Hangfire liberado apenas em Development ate a Fase 2 (JWT) introduzir a politica de admin.
/// Em qualquer outro ambiente o acesso e negado (fail-closed).
/// </summary>
internal sealed class DevelopmentOnlyDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetHttpContext().RequestServices
            .GetRequiredService<IHostEnvironment>()
            .IsDevelopment();
    }
}
