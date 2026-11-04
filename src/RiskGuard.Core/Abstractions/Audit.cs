namespace RiskGuard.Core.Abstractions;

/// <summary>Quem esta executando a operacao corrente: o <c>sub</c> do token do provedor de identidade, ou nada (jobs e CLI).</summary>
public interface IActorContext
{
    string? ActorId { get; }
}

public interface IActorContextSetter
{
    void SetActor(string actorId);
}
