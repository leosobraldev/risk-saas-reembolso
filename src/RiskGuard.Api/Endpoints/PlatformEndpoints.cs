using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Identity;

namespace RiskGuard.Api.Endpoints;

internal static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // GET /api/v1/platforms: as plataformas de e-commerce suportadas e o que a interface precisa saber de cada uma. Dados estaticos
        // (nada do lojista): o painel monta o formulario de conexao a partir daqui, sem lista fixa no frontend.
        app.MapGet("/api/v1/platforms", () => Results.Ok(PlatformCatalog.All))
            .RequireAuthorization(IdentityAuthentication.TenantPolicy);

        return app;
    }
}
