using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Tenant.Domain;

public enum TenantUserRole
{
    Owner,
}

/// <summary>
/// Liga um usuario do provedor de identidade (<c>sub</c> do token) a um lojista. Hoje cada lojista tem um dono; o modelo ja permite
/// varios usuarios por lojista sem mexer no restante (autorizacao continua baseada no claim de tenant do token).
/// </summary>
public sealed class TenantUser : Entity, ITenantEntity
{
    private TenantUser()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    /// <summary>Identificador do usuario no provedor (ex.: <c>auth0|65f...</c>).</summary>
    public string ExternalUserId { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public TenantUserRole Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TenantUser CreateOwner(Guid tenantId, string externalUserId, string email, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(externalUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new TenantUser
        {
            TenantId = tenantId,
            ExternalUserId = externalUserId,
            Email = EmailRules.Normalize(email),
            Role = TenantUserRole.Owner,
            CreatedAt = now,
        };
    }
}
