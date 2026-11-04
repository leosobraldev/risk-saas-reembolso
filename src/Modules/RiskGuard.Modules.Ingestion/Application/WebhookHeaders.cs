using System.Text.Json;

namespace RiskGuard.Modules.Ingestion.Application;

/// <summary>Politica de persistencia dos headers do webhook: sem credenciais e com tamanho limitado.</summary>
public static class WebhookHeaders
{
    public const int MaxHeaders = 100;
    public const int MaxValueLength = 1024;

    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Auth-Token",
    };

    public static string ToJson(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var safe = headers
            .Where(h => !Denied.Contains(h.Key))
            .Take(MaxHeaders)
            .ToDictionary(
                h => h.Key,
                h => h.Value.Length <= MaxValueLength ? h.Value : h.Value[..MaxValueLength],
                StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(safe);
    }
}
