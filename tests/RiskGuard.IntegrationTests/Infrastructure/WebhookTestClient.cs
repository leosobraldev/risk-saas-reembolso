using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace RiskGuard.IntegrationTests.Infrastructure;

/// <summary>Credenciais geradas para o lojista de teste em uma plataforma (os segredos reais so existem cifrados no banco).</summary>
public sealed record TestCredentials(string AccountId, string WebhookSecret, string AccessToken);

public static class TestCredentialStore
{
    private static readonly ConcurrentDictionary<(Guid TenantId, string Platform), TestCredentials> Store = new();

    public static void Set(Guid tenantId, string platform, TestCredentials credentials) => Store[(tenantId, platform)] = credentials;

    public static TestCredentials? Get(Guid tenantId, string platform) => Store.TryGetValue((tenantId, platform), out var c) ? c : null;

    public static TestCredentials NewCredentials(string platform) => new(
        platform == "shopify" ? $"loja-{Guid.NewGuid():N}"[..14] + ".myshopify.com" : Random.Shared.Next(100_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
        $"whsec_{Guid.NewGuid():N}",
        $"tok_{Guid.NewGuid():N}");
}

/// <summary>Envia webhooks como as plataformas fazem: corpo cru + HMAC-SHA256 do corpo com o segredo do lojista.</summary>
public static class WebhookTestClient
{
    public static string ShopifyHeader => "X-Shopify-Hmac-SHA256";

    public static string NuvemshopHeader => "x-linkedstore-hmac-sha256";

    /// <summary>Assinatura no formato da plataforma (Shopify: base64; Nuvemshop: hex).</summary>
    public static string Sign(string platform, byte[] body, string secret)
    {
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return platform == "nuvemshop" ? Convert.ToHexString(digest).ToLowerInvariant() : Convert.ToBase64String(digest);
    }

    /// <param name="secretOverride">Assina com outro segredo (cenarios de assinatura invalida).</param>
    /// <param name="sign">Falso: envia sem assinatura.</param>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string platform,
        Guid tenantId,
        string body,
        bool sign = true,
        string? secretOverride = null,
        params (string Name, string Value)[] headers)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/webhooks/{platform}/{tenantId}", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var secret = secretOverride ?? TestCredentialStore.Get(tenantId, platform)?.WebhookSecret;
        if (sign && secret is not null)
        {
            request.Headers.TryAddWithoutValidation(platform == "nuvemshop" ? NuvemshopHeader : ShopifyHeader, Sign(platform, bytes, secret));
        }

        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request);
    }
}
