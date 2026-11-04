using MediatR;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;

namespace RiskGuard.Api.Middleware;

/// <summary>
/// O token do provedor e valido ate expirar, mas o lojista pode ter sido desativado (ou nunca ter existido, no caso de um usuario
/// orfao do provedor). Para cada requisicao autenticada consulta o snapshot do tenant (cache Redis, banco no miss) e recusa com 403
/// se estiver inativo/inexistente: a desativacao vale na hora, sem esperar o token expirar.
/// </summary>
internal sealed class ActiveTenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ISender sender)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(sender);

        if (tenantContext.TenantId is { } tenantId)
        {
            var snapshot = await sender.Send(new GetTenantSnapshotQuery(tenantId), context.RequestAborted);
            if (snapshot is not { IsActive: true })
            {
                await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Lojista inativo ou inexistente.",
                    extensions: new Dictionary<string, object?> { ["code"] = "tenant.inactive" }).ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
