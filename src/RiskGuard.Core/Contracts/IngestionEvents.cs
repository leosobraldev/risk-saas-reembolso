using RiskGuard.Core.Domain;

namespace RiskGuard.Core.Contracts;

/// <summary>
/// Publicado pelo IngestionModule quando um pedido normalizado e persistido; consumido pelo RiskModule.
/// Entrega at-least-once: assinantes devem ser idempotentes por (TenantId, OrderId).
/// </summary>
public sealed record OrderIngestedEvent(
    Guid TenantId,
    Guid OrderId,
    string Platform,
    string ExternalOrderId,
    string CustomerEmail,
    decimal TotalAmount,
    DateTimeOffset OrderCreatedAt) : DomainEvent;
