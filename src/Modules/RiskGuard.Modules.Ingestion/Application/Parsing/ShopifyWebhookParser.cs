using System.Text.Json;

namespace RiskGuard.Modules.Ingestion.Application.Parsing;

/// <summary>Payload do webhook <c>orders/create</c> da Shopify.</summary>
public sealed class ShopifyWebhookParser : WebhookParserBase
{
    public override string Platform => "shopify";

    protected override string? ReadExternalOrderId(JsonElement root) => ReadString(root, "id");

    protected override string? ReadCustomerEmail(JsonElement root) =>
        ReadString(root, "email") ?? ReadString(root, "contact_email") ?? ReadString(root, "customer", "email");

    protected override decimal? ReadTotalAmount(JsonElement root) => ReadDecimal(root, "total_price");

    protected override DateTimeOffset? ReadCreatedAt(JsonElement root) => ReadDate(root, "created_at");
}
