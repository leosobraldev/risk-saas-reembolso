using MediatR;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Modules.Action.Application.Actions;
using RiskGuard.Modules.Action.Domain;

namespace RiskGuard.Api.Endpoints;

internal static class ActionEndpoints
{
    public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/actions").RequireAuthorization(IdentityAuthentication.TenantPolicy);

        // GET /api/v1/actions?page=1&pageSize=20&status=Succeeded
        group.MapGet(
            "/",
            async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20, OrderActionStatus? status = null) =>
                (await sender.Send(new ListOrderActionsQuery(page, pageSize, status), ct)).ToHttpResult(Results.Ok));

        // POST /api/v1/actions/{id}/release: libera um pedido bloqueado/sinalizado (falso positivo). Assincrono: 202.
        group.MapPost(
            "/{id:guid}/release",
            async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RequestOrderActionReleaseCommand(id), ct))
                    .ToHttpResult(action => Results.Accepted(uri: (string?)null, action)));

        return app;
    }
}
