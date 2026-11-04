using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Ingestion.Application.Parsing;

/// <summary>
/// Template Method: <see cref="Parse"/> e o esqueleto fixo (JSON valido -> leitura dos campos -> validacao e
/// normalizacao). Cada plataforma sobrescreve apenas os ganchos <c>Read*</c> que dizem ONDE o dado esta no payload.
/// </summary>
public abstract partial class WebhookParserBase : IWebhookParser
{
    public const int ExternalIdMaxLength = 200;

    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 32 };

    public abstract string Platform { get; }

    public Result<NormalizedOrder> Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Invalid("payload vazio");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return Invalid("JSON invalido");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("o payload deve ser um objeto JSON");
            }

            var incomplete = DetectIncompletePayload(root);
            if (incomplete is not null)
            {
                return incomplete;
            }

            var externalId = ReadExternalOrderId(root)?.Trim();
            if (string.IsNullOrEmpty(externalId) || externalId.Length > ExternalIdMaxLength)
            {
                return Invalid("campo obrigatorio ausente ou invalido: id");
            }

            var email = ReadCustomerEmail(root);
            if (!EmailRules.IsValid(email))
            {
                return Invalid("campo obrigatorio ausente ou invalido: email");
            }

            var total = ReadTotalAmount(root);
            if (total is null or < 0)
            {
                return Invalid("campo obrigatorio ausente ou invalido: total");
            }

            var createdAt = ReadCreatedAt(root);
            if (createdAt is null)
            {
                return Invalid("campo obrigatorio ausente ou invalido: created_at");
            }

            return new NormalizedOrder(
                Platform,
                externalId,
                EmailRules.Normalize(email),
                decimal.Round(total.Value, 2, MidpointRounding.ToEven),
                createdAt.Value.ToUniversalTime()); // PostgreSQL (timestamptz) so aceita offset zero
        }
    }

    // ---- ganchos especificos da plataforma ----

    /// <summary>
    /// Opcional: reconhece payloads que nao sao o pedido (so uma notificacao com o id) e devolve o erro que orienta o
    /// processamento a buscar o pedido na API da plataforma. Por padrao, nenhum payload e considerado incompleto.
    /// </summary>
    protected virtual Error? DetectIncompletePayload(JsonElement root) => null;

    protected abstract string? ReadExternalOrderId(JsonElement root);

    protected abstract string? ReadCustomerEmail(JsonElement root);

    protected abstract decimal? ReadTotalAmount(JsonElement root);

    protected abstract DateTimeOffset? ReadCreatedAt(JsonElement root);

    // ---- utilitarios de leitura tolerante (texto ou numero) ----

    protected static string? ReadString(JsonElement root, params string[] path)
    {
        var element = Navigate(root, path);
        return element?.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number => element.Value.GetRawText(),
            _ => null,
        };
    }

    protected static decimal? ReadDecimal(JsonElement root, params string[] path)
    {
        var element = Navigate(root, path);
        switch (element?.ValueKind)
        {
            case JsonValueKind.Number when element.Value.TryGetDecimal(out var number):
                return number;
            case JsonValueKind.String when decimal.TryParse(
                element.Value.GetString(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    /// <summary>ISO-8601 com offset; tambem aceita offset sem dois-pontos (ex.: +0000), usado pela Nuvemshop.</summary>
    protected static DateTimeOffset? ReadDate(JsonElement root, params string[] path)
    {
        var text = ReadString(root, path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = OffsetWithoutColon().Replace(text.Trim(), "$1:$2");
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }

    private static JsonElement? Navigate(JsonElement root, string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static Result<NormalizedOrder> Invalid(string reason) =>
        new Error("webhook.invalid_payload", $"Payload invalido: {reason}.", ErrorType.Validation);

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();
}
