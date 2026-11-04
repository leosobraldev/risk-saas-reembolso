using RiskGuard.Core.Abstractions;

namespace RiskGuard.Core.Domain;

/// <summary>Raiz base de entidades; acumula eventos de dominio para publicacao posterior.</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Guid.NewGuid();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
