using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Risk.Domain;

/// <summary>Historico do comprador dentro de um lojista (base das regras de historico).</summary>
public sealed class CustomerHistory : Entity, ITenantEntity
{
    private CustomerHistory()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;

    public int TotalRefunds { get; private set; }

    public static CustomerHistory Create(Guid tenantId, string customerEmail)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(customerEmail);

        return new CustomerHistory { TenantId = tenantId, CustomerEmail = EmailRules.Normalize(customerEmail) };
    }

    public void RecordRefund() => TotalRefunds++;
}
