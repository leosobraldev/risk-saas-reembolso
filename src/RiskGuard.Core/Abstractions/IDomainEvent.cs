using MediatR;

namespace RiskGuard.Core.Abstractions;

/// <summary>Evento de dominio publicado via MediatR para comunicacao entre modulos.</summary>
public interface IDomainEvent : INotification
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}
