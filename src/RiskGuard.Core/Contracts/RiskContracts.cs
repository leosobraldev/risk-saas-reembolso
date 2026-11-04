using RiskGuard.Core.Domain;

namespace RiskGuard.Core.Contracts;

public enum RiskLevel
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Publicado pelo RiskModule apos avaliar um pedido; consumido pelo ActionModule (Fase 5).
/// Entrega at-least-once: assinantes devem ser idempotentes por <see cref="EvaluationId"/>.
/// </summary>
public sealed record RiskCalculatedEvent(
    Guid TenantId,
    Guid EvaluationId,
    Guid OrderId,
    string Platform,
    string ExternalOrderId,
    int Score,
    RiskLevel Level) : DomainEvent;
