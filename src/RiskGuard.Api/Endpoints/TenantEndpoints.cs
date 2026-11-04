using MediatR;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Modules.Tenant.Application.Queries;

namespace RiskGuard.Api.Endpoints;

internal static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Login, MFA, renovacao e recuperacao de senha NAO existem aqui: sao do provedor de identidade (Auth0).
        // GET /api/v1/tenants/me: lojista autenticado (JWT do provedor + contexto de tenant + RLS).
        app.MapGet("/api/v1/tenants/me", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetCurrentTenantQuery(), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(IdentityAuthentication.TenantPolicy);

        return app;
    }
}
