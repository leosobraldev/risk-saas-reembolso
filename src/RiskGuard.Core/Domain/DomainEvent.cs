using RiskGuard.Core.Abstractions;

namespace RiskGuard.Core.Domain;

/// <summary>Base imutavel para eventos de dominio.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
