using MediatR;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Modules.Risk.Application.Queries;

namespace RiskGuard.Api.Endpoints;

internal static class RiskEndpoints
{
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // GET /api/v1/risk/evaluations?page=1&pageSize=20&level=High (SDD 8: dashboard)
        app.MapGet(
                "/api/v1/risk/evaluations",
                async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20, RiskLevel? level = null) =>
                    (await sender.Send(new ListRiskEvaluationsQuery(page, pageSize, level), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(IdentityAuthentication.TenantPolicy);

        return app;
    }
}
