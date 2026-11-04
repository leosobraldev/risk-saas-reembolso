using System.Security.Claims;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Api.Middleware;

/// <summary>
/// Propaga o <c>tenant_id</c> do JWT validado para o escopo da requisicao (alimenta o Global Query Filter e a RLS).
/// Sem claim valida o escopo fica sem tenant e o acesso a dados tenant-scoped retorna vazio (fail-closed).
/// </summary>
internal sealed class TenantContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, ITenantContextSetter tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(RiskGuardClaims.TenantId), out var tenantId)
            && tenantId != Guid.Empty)
        {
            tenantContext.SetTenant(tenantId);
        }

        return next(context);
    }
}
