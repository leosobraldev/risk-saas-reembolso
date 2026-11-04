using MediatR;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Modules.Tenant.Application.Privacy;

namespace RiskGuard.Api.Endpoints;

internal static class PrivacyEndpoints
{
    public sealed record AnonymizeRequest(string? Email);

    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Direito de eliminacao (LGPD): o lojista pede a anonimizacao dos dados de um comprador. Idempotente.
        app.MapPost(
                "/api/v1/privacy/customers/anonymize",
                async (AnonymizeRequest body, ISender sender, CancellationToken ct) =>
                    (await sender.Send(new AnonymizeCustomerCommand(body.Email), ct)).ToHttpResult(() => Results.NoContent()))
            .RequireAuthorization(IdentityAuthentication.TenantPolicy);

        return app;
    }
}
