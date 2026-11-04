using System.Text.Json;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Modules.Ingestion.Application.Parsing;

/// <summary>
/// Recurso de pedido da Nuvemshop. Notificacoes que trazem apenas <c>{ store_id, event, id }</c> nao contem os dados do
/// pedido: sao sinalizadas com <see cref="WebhookErrorCodes.NeedsOrderFetch"/> e o pedido e buscado na API da loja.
/// </summary>
public sealed class NuvemshopWebhookParser : WebhookParserBase
{
    public override string Platform => "nuvemshop";

    protected override Error? DetectIncompletePayload(JsonElement root) =>
        root.TryGetProperty("event", out _) && root.TryGetProperty("id", out _)
        && !root.TryGetProperty("total", out _) && !root.TryGetProperty("contact_email", out _)
            ? new Error(WebhookErrorCodes.NeedsOrderFetch, "O webhook traz so o id do pedido; os dados precisam ser buscados na API da loja.", ErrorType.Validation)
            : null;

    protected override string? ReadExternalOrderId(JsonElement root) => ReadString(root, "id");

    protected override string? ReadCustomerEmail(JsonElement root) =>
        ReadString(root, "contact_email") ?? ReadString(root, "customer", "email");

    protected override decimal? ReadTotalAmount(JsonElement root) => ReadDecimal(root, "total");

    protected override DateTimeOffset? ReadCreatedAt(JsonElement root) => ReadDate(root, "created_at");
}
