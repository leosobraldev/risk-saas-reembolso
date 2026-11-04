using RiskGuard.Core.Abstractions;

namespace RiskGuard.Infrastructure.Tenancy;

/// <summary>Tenant corrente (escopo da requisicao ou do job).</summary>
public sealed class TenantContext : ITenantContext, ITenantContextSetter, IActorContext, IActorContextSetter
{
    public Guid? TenantId { get; private set; }

    public string? ActorId { get; private set; }

    public void SetActor(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ActorId = actorId;
    }

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        // Um escopo nunca "troca" de tenant: impede que codigo autenticado como A passe a operar como B.
        if (TenantId is { } current && current != tenantId)
        {
            throw new InvalidOperationException("O escopo ja pertence a outro tenant.");
        }

        TenantId = tenantId;
    }
}
