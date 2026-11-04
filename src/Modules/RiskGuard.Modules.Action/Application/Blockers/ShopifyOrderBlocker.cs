using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Modules.Action.Domain;

namespace RiskGuard.Modules.Action.Application.Blockers;

/// <summary>
/// Shopify Admin REST: lista as fulfillment orders do pedido e coloca em hold (<c>hold.json</c>, motivo
/// <c>high_risk_of_fraud</c>) as que ainda nao foram cumpridas; a liberacao usa <c>release_hold.json</c>. A idempotencia vem do
/// estado (so atua em fulfillment orders open/scheduled; so libera on_hold). Token no header <c>X-Shopify-Access-Token</c>.
/// Observacao da Shopify: a API REST Admin e legada; apps publicos novos devem usar a GraphQL Admin (migracao futura).
/// </summary>
public sealed partial class ShopifyOrderBlocker(IHttpClientFactory httpClientFactory, IOptions<PlatformApisOptions> options) : IOrderBlocker
{
    public const string DefaultApiVersion = "2026-10";
    public const string NotePrefix = "RiskGuard:";

    private const int NotesMaxLength = 250;

    public string Platform => PlatformIds.Shopify;

    public OrderActionType ActionType => OrderActionType.Block;

    public async Task<ExternalApiResult> BlockAsync(BlockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryPrepare(request, out var api, out var failure))
        {
            return failure!;
        }

        var listed = await ListFulfillmentOrdersAsync(api!, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            return listed.Failure!;
        }

        var orders = listed.Orders!;
        if (orders.Count == 0)
        {
            return ExternalApiResult.Permanent("Nenhuma fulfillment order acessivel (pedido inexistente, sem escopo de acesso ou ja cumprido).");
        }

        var holdable = orders.Where(o => o.Status is "open" or "scheduled").ToList();
        if (holdable.Count == 0)
        {
            return orders.Any(o => o.Status == "on_hold")
                ? ExternalApiResult.Success() // ja estava em hold
                : ExternalApiResult.Permanent($"Nenhuma fulfillment order pode ser pausada (status: {string.Join(", ", orders.Select(o => o.Status))}).");
        }

        var notes = Truncate($"{NotePrefix} {request.Reason}");
        foreach (var order in holdable)
        {
            var result = await PostAsync(
                api!,
                $"fulfillment_orders/{order.Id}/hold.json",
                new { fulfillment_hold = new { reason = "high_risk_of_fraud", reason_notes = notes, notify_merchant = false } },
                cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return result; // transitoria: o retry refaz a listagem e so atua no que ainda esta open
            }
        }

        return ExternalApiResult.Success();
    }

    public async Task<ExternalApiResult> ReleaseAsync(BlockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryPrepare(request, out var api, out var failure))
        {
            return failure!;
        }

        var listed = await ListFulfillmentOrdersAsync(api!, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            return listed.Failure!;
        }

        var onHold = listed.Orders!.Where(o => o.Status == "on_hold").ToList();

        // release_hold.json libera TODOS os holds da fulfillment order: so o fazemos se todos forem nossos.
        if (onHold.Any(o => o.Holds.Any(note => !note.StartsWith(NotePrefix, StringComparison.Ordinal))))
        {
            return ExternalApiResult.Permanent("O pedido tem holds de outra origem; libere manualmente na Shopify para nao liberar holds do lojista.");
        }

        foreach (var order in onHold)
        {
            var result = await PostAsync(api!, $"fulfillment_orders/{order.Id}/release_hold.json", new { }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        return ExternalApiResult.Success(); // nada em hold = nada a liberar
    }

    // ---- infraestrutura HTTP ----

    private sealed record Api(Uri Root, string Version, string Token, string OrderId);

    private sealed record FulfillmentOrder(string Id, string Status, IReadOnlyList<string> Holds);

    private sealed record Listing(IReadOnlyList<FulfillmentOrder>? Orders, ExternalApiResult? Failure)
    {
        public bool IsSuccess => Failure is null;
    }

    private bool TryPrepare(BlockRequest request, out Api? api, out ExternalApiResult? failure)
    {
        api = null;
        var credentials = request.Credentials;

        if (string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            failure = ExternalApiResult.Permanent("Lojista sem token de acesso da Shopify: conecte a loja para habilitar o bloqueio.");
            return false;
        }

        // O host vem do dominio da loja: so *.myshopify.com valido (nunca uma URL qualquer vinda do tenant).
        if (!PlatformIds.IsValidAccountId(Platform, credentials.AccountId) || !OrderIdPattern().IsMatch(request.ExternalOrderId))
        {
            failure = ExternalApiResult.Permanent("Dominio da loja ou identificador do pedido invalido.");
            return false;
        }

        var endpoint = options.Value.For(Platform);
        var root = endpoint.BaseUrl ?? new Uri($"https://{credentials.AccountId}/");
        api = new Api(root, endpoint.ApiVersion ?? DefaultApiVersion, credentials.AccessToken, request.ExternalOrderId);
        failure = null;
        return true;
    }

    private async Task<Listing> ListFulfillmentOrdersAsync(Api api, CancellationToken cancellationToken)
    {
        using var message = Build(api, HttpMethod.Get, $"orders/{api.OrderId}/fulfillment_orders.json");
        using var client = httpClientFactory.CreateClient(Platform);
        var result = await ExternalApiCaller.SendAsync(client, message, Platform, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return new Listing(null, result);
        }

        try
        {
            using var document = JsonDocument.Parse(result.Body ?? "{}");
            var orders = new List<FulfillmentOrder>();
            if (document.RootElement.TryGetProperty("fulfillment_orders", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetRawText().Trim('"') : string.Empty;
                    if (!FulfillmentOrderIdPattern().IsMatch(id))
                    {
                        continue;
                    }

                    var status = item.TryGetProperty("status", out var s) ? (s.GetString() ?? string.Empty).ToLowerInvariant() : string.Empty;
                    var holds = new List<string>();
                    if (item.TryGetProperty("fulfillment_holds", out var holdArray) && holdArray.ValueKind == JsonValueKind.Array)
                    {
                        holds.AddRange(holdArray.EnumerateArray().Select(h => h.TryGetProperty("reason_notes", out var n) ? n.GetString() ?? string.Empty : string.Empty));
                    }

                    orders.Add(new FulfillmentOrder(id, status, holds));
                }
            }

            return new Listing(orders, null);
        }
        catch (JsonException)
        {
            return new Listing(null, ExternalApiResult.Permanent("Resposta invalida da Shopify ao listar fulfillment orders."));
        }
    }

    private async Task<ExternalApiResult> PostAsync(Api api, string path, object body, CancellationToken cancellationToken)
    {
        using var message = Build(api, HttpMethod.Post, path);
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var client = httpClientFactory.CreateClient(Platform);
        return await ExternalApiCaller.SendAsync(client, message, Platform, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage Build(Api api, HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, new Uri(api.Root, $"admin/api/{api.Version}/{path}"));
        message.Headers.TryAddWithoutValidation("X-Shopify-Access-Token", api.Token);
        return message;
    }

    private static string Truncate(string value) => value.Length <= NotesMaxLength ? value : value[..NotesMaxLength];

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex OrderIdPattern();

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex FulfillmentOrderIdPattern();
}
