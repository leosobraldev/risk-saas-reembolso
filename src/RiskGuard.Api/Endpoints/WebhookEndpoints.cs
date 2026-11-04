using MediatR;
using RiskGuard.Modules.Ingestion.Application.Commands;

namespace RiskGuard.Api.Endpoints;

internal static class WebhookEndpoints
{
    /// <summary>Teto do corpo aceito (payloads de pedido sao pequenos); acima disso retornamos 413.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    public sealed record AcceptedResponse(Guid WebhookId);

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // POST /api/v1/webhooks/{platform}/{tenantId}: responde 202 assim que o raw payload e o job estao persistidos.
        app.MapPost(
                "/api/v1/webhooks/{platform}/{tenantId:guid}",
                async (string platform, Guid tenantId, HttpRequest request, ISender sender, CancellationToken ct) =>
                {
                    var body = await ReadBodyAsync(request, ct);
                    if (body is null)
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status413PayloadTooLarge,
                            title: "Corpo da requisicao excede o limite permitido.",
                            extensions: new Dictionary<string, object?> { ["code"] = "webhook.payload_too_large" });
                    }

                    var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

                    return (await sender.Send(new IngestWebhookCommand(platform, tenantId, body, headers), ct))
                        .ToHttpResult(id => Results.Accepted(uri: (string?)null, new AcceptedResponse(id)));
                })
            .AllowAnonymous();

        return app;
    }

    /// <returns>Os bytes CRUS do corpo (a assinatura HMAC so vale sobre eles), ou <c>null</c> se exceder <see cref="MaxBodyBytes"/>.</returns>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null; // corpo "chunked" sem Content-Length
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
