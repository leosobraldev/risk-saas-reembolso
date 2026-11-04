using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Faz o papel do Auth0 nos testes: um emissor de tokens RS256 com chave de teste. A API nao conhece "login"; ela so valida o JWT do
/// provedor, entao os testes emitem tokens no MESMO formato (emissor, audiencia e claim namespaced do lojista).
/// </summary>
public static class TestIdentity
{
    public const string Issuer = "https://riskguard-tests.local/";
    public const string Audience = "https://api.riskguard.test";
    public const string TenantClaim = "https://riskguard.test/tenant_id";

    public static RsaSecurityKey SigningKey { get; } = NewKey("test-key-1");

    /// <summary>Chave de um atacante: tokens assinados com ela nao podem ser aceitos.</summary>
    public static RsaSecurityKey AttackerKey { get; } = NewKey("test-key-1"); // mesmo kid de proposito

    private static RsaSecurityKey NewKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };

    public sealed record TokenSpec
    {
        public Guid? TenantId { get; init; }

        /// <summary>Claim do lojista (padrao: o namespaced configurado). Mude para simular configuracao errada.</summary>
        public string TenantClaimName { get; init; } = TenantClaim;

        public string? Subject { get; init; }

        public string IssuerValue { get; init; } = Issuer;

        public string AudienceValue { get; init; } = Audience;

        public RsaSecurityKey? Key { get; init; }

        public DateTime? NotBefore { get; init; }

        public DateTime? Expires { get; init; }

        public string Algorithm { get; init; } = SecurityAlgorithms.RsaSha256;

        public IReadOnlyDictionary<string, object>? ExtraClaims { get; init; }
    }

    /// <summary>Token de acesso de um usuario do lojista, como o Auth0 o emitiria (com a Action de post-login aplicada).</summary>
    public static string CreateToken(Guid tenantId) => CreateToken(new TokenSpec { TenantId = tenantId });

    public static string CreateToken(TokenSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var claims = new Dictionary<string, object> { ["sub"] = spec.Subject ?? $"auth0|{spec.TenantId?.ToString("N") ?? "m2m"}" };
        if (spec.TenantId is { } tenantId)
        {
            claims[spec.TenantClaimName] = tenantId.ToString();
        }

        foreach (var (name, value) in spec.ExtraClaims ?? new Dictionary<string, object>())
        {
            claims[name] = value;
        }

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = spec.IssuerValue,
            Audience = spec.AudienceValue,
            NotBefore = spec.NotBefore ?? now.AddMinutes(-1),
            Expires = spec.Expires ?? now.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(spec.Key ?? SigningKey, spec.Algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Nome do claim interno (para asserts): o unico que o resto do sistema le.</summary>
    public static string InternalTenantClaim => RiskGuardClaims.TenantId;
}
