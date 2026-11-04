using MediatR;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Modules.Tenant.Application.Audit;

namespace RiskGuard.Api.Endpoints;

internal static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // GET /api/v1/audit-events?page=1&pageSize=20&action=settings.updated: so leitura; a trilha nao tem rota de escrita.
        app.MapGet(
                "/api/v1/audit-events",
                async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20, string? action = null) =>
                    (await sender.Send(new ListAuditEventsQuery(page, pageSize, action), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(IdentityAuthentication.TenantPolicy);

        return app;
    }
}
