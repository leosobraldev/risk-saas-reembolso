using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Ingestion.Domain;

/// <summary>Pedido normalizado, independente da plataforma de origem.</summary>
public sealed class Order : Entity, ITenantEntity
{
    private Order()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    /// <summary>Plataforma de origem (parte da chave de idempotencia: tenant + plataforma + id externo).</summary>
    public string Platform { get; private set; } = string.Empty;

    public string ExternalOrderId { get; private set; } = string.Empty;

    public string CustomerEmail { get; private set; } = string.Empty;

    public decimal TotalAmount { get; private set; }

    /// <summary>Momento de criacao do pedido na plataforma de origem.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public static Order Create(Guid tenantId, string platform, string externalOrderId, string customerEmail, decimal totalAmount, DateTimeOffset createdAt) =>
        new()
        {
            TenantId = tenantId,
            Platform = platform,
            ExternalOrderId = externalOrderId,
            CustomerEmail = customerEmail,
            TotalAmount = totalAmount,
            CreatedAt = createdAt,
        };
}
