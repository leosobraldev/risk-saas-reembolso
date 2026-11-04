using System.Security.Claims;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Api.Middleware;

/// <summary>
/// Propaga o <c>tenant_id</c> do JWT validado para o escopo da requisicao (alimenta o Global Query Filter e a RLS).
/// Sem claim valida o escopo fica sem tenant e o acesso a dados tenant-scoped retorna vazio (fail-closed).
/// </summary>
internal sealed class TenantContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, ITenantContextSetter tenantContext, IActorContextSetter actorContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(actorContext);

        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(RiskGuardClaims.TenantId), out var tenantId)
            && tenantId != Guid.Empty)
        {
            tenantContext.SetTenant(tenantId);

            // Quem esta agindo (para a trilha de auditoria): o sub do provedor.
            if (context.User.FindFirstValue("sub") is { Length: > 0 } actor)
            {
                actorContext.SetActor(actor);
            }
        }

        return next(context);
    }
}
