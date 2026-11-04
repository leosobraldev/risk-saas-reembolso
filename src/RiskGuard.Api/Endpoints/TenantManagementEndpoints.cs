using MediatR;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Identity;
using RiskGuard.Modules.Tenant.Application.Connections;
using RiskGuard.Modules.Tenant.Application.Settings;

namespace RiskGuard.Api.Endpoints;

internal static class TenantManagementEndpoints
{
    public sealed record UpdateSettingsRequest(int? HighRiskThreshold, bool? AutoBlockEnabled);

    public sealed record UpsertConnectionRequest(string? AccountId, string? WebhookSecret, string? AccessToken);

    public static IEndpointRouteBuilder MapTenantManagementEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/tenants/me").RequireAuthorization(IdentityAuthentication.TenantPolicy);

        // Limite de risco alto e bloqueio automatico. Invalida o cache do tenant: vale para os proximos pedidos, ja.
        group.MapPut(
            "/settings",
            async (UpdateSettingsRequest body, ISender sender, CancellationToken ct) =>
                body is { HighRiskThreshold: { } threshold, AutoBlockEnabled: { } autoBlock }
                    ? (await sender.Send(new UpdateTenantSettingsCommand(threshold, autoBlock), ct)).ToHttpResult(Results.Ok)
                    : Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Informe highRiskThreshold e autoBlockEnabled.",
                        extensions: new Dictionary<string, object?> { ["code"] = "settings.invalid_request" }));

        // Conexoes com as plataformas (credenciais por loja). Os segredos so entram: nenhuma resposta os devolve.
        group.MapGet(
            "/connections",
            async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListPlatformConnectionsQuery(), ct)).ToHttpResult(Results.Ok));

        group.MapPut(
            "/connections/{platform}",
            async (string platform, UpsertConnectionRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpsertPlatformConnectionCommand(platform, body.AccountId, body.WebhookSecret, body.AccessToken), ct))
                    .ToHttpResult(Results.Ok));

        group.MapDelete(
            "/connections/{platform}",
            async (string platform, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RemovePlatformConnectionCommand(platform), ct)).ToHttpResult(() => Results.NoContent()));

        return app;
    }
}
