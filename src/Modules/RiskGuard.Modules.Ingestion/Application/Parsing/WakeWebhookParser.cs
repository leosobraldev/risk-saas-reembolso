using System.Text.Json;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Modules.Ingestion.Application.Parsing;

/// <summary>
/// Wake (Wake Commerce, ex-Fbits). O webhook de <c>pedido.novo</c> traz so <c>{ topico, evento: { loja, pedido, ... } }</c>: nao ha
/// e-mail nem valor. A notificacao e sinalizada com <see cref="WebhookErrorCodes.NeedsOrderFetch"/> e o pedido e buscado em
/// <c>GET /pedidos/{id}</c> (<see cref="WakeOrderFetcher"/>); a resposta passa por este mesmo parser. Notificacoes de outros topicos
/// (situacao, rastreamento, indexacao, ...) nao sao pedidos novos: sao <see cref="WebhookErrorCodes.Ignored"/>, nao falha.
/// </summary>
public sealed class WakeWebhookParser : WebhookParserBase
{
    public const string NewOrderTopic = "pedido.novo";

    public override string Platform => PlatformIds.Wake;

    protected override Error? DetectIncompletePayload(JsonElement root)
    {
        // O pedido buscado na API nao tem "topico"; so a notificacao tem.
        if (!root.TryGetProperty("topico", out var topic))
        {
            return null;
        }

        if (topic.ValueKind != JsonValueKind.String || topic.GetString() != NewOrderTopic)
        {
            return new Error(WebhookErrorCodes.Ignored, "Notificacao de outro tipo de evento: nada a avaliar.", ErrorType.Validation);
        }

        // Sem o numero do pedido nao ha o que buscar: payload invalido (nao tenta buscar no vazio).
        return ReadNotificationOrderId(root) is null
            ? new Error("webhook.invalid_payload", "Payload invalido: notificacao sem o numero do pedido (evento.pedido).", ErrorType.Validation)
            : new Error(WebhookErrorCodes.NeedsOrderFetch, "O webhook traz so o numero do pedido; os dados precisam ser buscados na API da Wake.", ErrorType.Validation);
    }

    protected override string? ReadNotificationOrderId(JsonElement root) => ReadString(root, "evento", "pedido");

    protected override string? ReadExternalOrderId(JsonElement root) => ReadString(root, "pedidoId");

    protected override string? ReadCustomerEmail(JsonElement root) => ReadString(root, "usuario", "email");

    protected override decimal? ReadTotalAmount(JsonElement root) => ReadDecimal(root, "valorTotalPedido");

    protected override DateTimeOffset? ReadCreatedAt(JsonElement root) => ReadDate(root, "data");
}
