using System.Security.Cryptography;
using System.Text;

namespace RiskGuard.Modules.Ingestion.Application.Signatures;

/// <summary>Strategy: como cada plataforma assina seus webhooks.</summary>
public interface IWebhookSignatureVerifier
{
    string Platform { get; }

    /// <param name="headers">Headers da requisicao (busca sem diferenciar maiusculas).</param>
    /// <param name="body">Corpo CRU exatamente como recebido (qualquer reserializacao invalida a assinatura).</param>
    /// <param name="secret">Segredo do lojista para esta plataforma.</param>
    bool IsValid(IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, string secret);
}

public interface IWebhookSignatureVerifierResolver
{
    IWebhookSignatureVerifier? Resolve(string? platform);
}

public sealed class WebhookSignatureVerifierResolver : IWebhookSignatureVerifierResolver
{
    private readonly Dictionary<string, IWebhookSignatureVerifier> _verifiers = new(StringComparer.OrdinalIgnoreCase);

    public WebhookSignatureVerifierResolver(IEnumerable<IWebhookSignatureVerifier> verifiers)
    {
        ArgumentNullException.ThrowIfNull(verifiers);

        foreach (var verifier in verifiers)
        {
            if (!_verifiers.TryAdd(verifier.Platform, verifier))
            {
                throw new InvalidOperationException($"Verificador de assinatura '{verifier.Platform}' registrado mais de uma vez.");
            }
        }
    }

    public IWebhookSignatureVerifier? Resolve(string? platform) =>
        platform is not null && _verifiers.TryGetValue(platform.Trim(), out var verifier) ? verifier : null;
}

/// <summary>
/// Template Method: HMAC-SHA256 do corpo cru com o segredo do lojista, comparado em tempo constante. Cada plataforma informa
/// apenas o nome do header e como a assinatura vem codificada.
/// </summary>
public abstract class HmacWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    public abstract string Platform { get; }

    protected abstract string HeaderName { get; }

    /// <returns>Os bytes do digest, ou <c>null</c> se o valor do header nao estiver no formato esperado.</returns>
    protected abstract byte[]? DecodeSignature(string headerValue);

    public bool IsValid(IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, string secret)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        var header = headers.FirstOrDefault(h => h.Key.Equals(HeaderName, StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        var provided = DecodeSignature(header.Trim());
        if (provided is null)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}

/// <summary>Shopify: <c>X-Shopify-Hmac-SHA256</c>, digest em base64, chave = segredo do app.</summary>
public sealed class ShopifySignatureVerifier : HmacWebhookSignatureVerifier
{
    public override string Platform => "shopify";

    protected override string HeaderName => "X-Shopify-Hmac-SHA256";

    protected override byte[]? DecodeSignature(string headerValue) => TryBase64(headerValue);

    internal static byte[]? TryBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Nuvemshop: <c>x-linkedstore-hmac-sha256</c>. A documentacao nao diz se o digest e hex ou base64 (o exemplo oficial usa o
/// padrao do PHP, hex); aceitamos os dois, que sao apenas codificacoes do mesmo digest.
/// </summary>
public sealed class NuvemshopSignatureVerifier : HmacWebhookSignatureVerifier
{
    public override string Platform => "nuvemshop";

    protected override string HeaderName => "x-linkedstore-hmac-sha256";

    protected override byte[]? DecodeSignature(string headerValue)
    {
        try
        {
            return Convert.FromHexString(headerValue);
        }
        catch (FormatException)
        {
            return ShopifySignatureVerifier.TryBase64(headerValue);
        }
    }
}
