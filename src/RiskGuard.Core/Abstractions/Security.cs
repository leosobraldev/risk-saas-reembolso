namespace RiskGuard.Core.Abstractions;

/// <summary>Nomes de claims do JWT do RiskGuard.</summary>
public static class RiskGuardClaims
{
    public const string TenantId = "tenant_id";
}

/// <summary>
/// Cifra segredos de terceiros (tokens de API, segredos de webhook) antes de persistir. O <paramref name="context"/> e
/// autenticado junto (AEAD): um valor cifrado para uma linha nao pode ser reaproveitado em outra.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext, string context);

    string Unprotect(string protectedValue, string context);
}

/// <summary>Permite ao fluxo de cadastro (unico caso sem tenant autenticado) definir o tenant do escopo.</summary>
public interface ITenantContextSetter
{
    void SetTenant(Guid tenantId);
}

/// <summary>Aplica as migrations de um modulo.</summary>
public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
