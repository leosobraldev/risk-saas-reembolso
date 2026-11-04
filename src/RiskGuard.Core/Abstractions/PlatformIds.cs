using System.Text.RegularExpressions;

namespace RiskGuard.Core.Abstractions;

/// <summary>Plataformas de e-commerce suportadas e as regras de validacao da conta de cada uma.</summary>
public static partial class PlatformIds
{
    public const string Shopify = "shopify";
    public const string Nuvemshop = "nuvemshop";

    public static IReadOnlyList<string> All { get; } = [Shopify, Nuvemshop];

    public static bool IsSupported(string? platform) =>
        platform is not null && All.Contains(platform.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valida o identificador da conta na plataforma. Importante para seguranca: o host das chamadas externas e derivado
    /// deste valor (Shopify), entao so dominios <c>*.myshopify.com</c> sao aceitos (evita SSRF por dado do tenant).
    /// </summary>
    public static bool IsValidAccountId(string? platform, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        return platform?.Trim().ToLowerInvariant() switch
        {
            Shopify => ShopifyDomain().IsMatch(accountId),
            Nuvemshop => NuvemshopStoreId().IsMatch(accountId),
            _ => false,
        };
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{0,60}\.myshopify\.com$")]
    private static partial Regex ShopifyDomain();

    [GeneratedRegex(@"^\d{1,15}$")]
    private static partial Regex NuvemshopStoreId();
}
