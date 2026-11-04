using RiskGuard.Core.Abstractions;

namespace RiskGuard.Core.Contracts;

/// <summary>
/// Credenciais do lojista em uma plataforma, JA DECIFRADAS, para uso em memoria por outros modulos (verificar a assinatura do
/// webhook, chamar a API da loja). Nunca persistir, logar nem devolver em respostas de API; por isso nao ha cache em Redis.
/// </summary>
public sealed record PlatformCredentials(string Platform, string AccountId, string? WebhookSecret, string? AccessToken)
{
    public override string ToString() => $"PlatformCredentials {{ Platform = {Platform}, AccountId = {AccountId} }}"; // sem segredos
}

/// <summary>Retorna <c>null</c> quando o lojista nao conectou a plataforma.</summary>
public sealed record GetPlatformCredentialsQuery(Guid TenantId, string Platform) : IQuery<PlatformCredentials?>;
