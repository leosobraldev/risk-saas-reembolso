using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Tenant.Domain;

/// <summary>
/// Conexao do lojista com uma plataforma de e-commerce: conta + segredos. Os segredos so existem CIFRADOS aqui
/// (o dominio nunca ve o texto puro; quem cifra e o caso de uso).
/// </summary>
public sealed class PlatformConnection : Entity, ITenantEntity
{
    private PlatformConnection()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    public string Platform { get; private set; } = string.Empty;

    /// <summary>Dominio da loja (Shopify) ou id da loja (Nuvemshop).</summary>
    public string AccountId { get; private set; } = string.Empty;

    /// <summary>Segredo usado para verificar a assinatura dos webhooks (cifrado).</summary>
    public string WebhookSecretProtected { get; private set; } = string.Empty;

    /// <summary>Token de acesso a API da loja (cifrado); opcional ate o lojista autorizar o app.</summary>
    public string? AccessTokenProtected { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PlatformConnection Create(
        Guid tenantId,
        string platform,
        string accountId,
        string webhookSecretProtected,
        string? accessTokenProtected,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookSecretProtected);

        return new PlatformConnection
        {
            TenantId = tenantId,
            Platform = platform.Trim().ToLowerInvariant(),
            AccountId = accountId.Trim(),
            WebhookSecretProtected = webhookSecretProtected,
            AccessTokenProtected = accessTokenProtected,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Replace(string accountId, string webhookSecretProtected, string? accessTokenProtected, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookSecretProtected);

        AccountId = accountId.Trim();
        WebhookSecretProtected = webhookSecretProtected;
        AccessTokenProtected = accessTokenProtected;
        UpdatedAt = now;
    }

    /// <summary>Contextos AEAD: amarram cada segredo cifrado ao tenant, a plataforma e a finalidade.</summary>
    public static string WebhookSecretContext(Guid tenantId, string platform) => $"{tenantId}:{platform.Trim().ToLowerInvariant()}:webhook-secret";

    public static string AccessTokenContext(Guid tenantId, string platform) => $"{tenantId}:{platform.Trim().ToLowerInvariant()}:access-token";
}
