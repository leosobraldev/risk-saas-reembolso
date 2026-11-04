using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Tenant.Domain;

/// <summary>
/// Fato imutavel: quem fez o que, em que alvo e quando. Sem metodos de alteracao; no banco o papel da aplicacao so tem INSERT e
/// SELECT (e DELETE apenas de eventos mais velhos que o periodo minimo de guarda, pela policy de retencao).
/// </summary>
public sealed class AuditEvent : Entity, ITenantEntity
{
    public const string SystemActor = "system";
    public const int ActorMaxLength = 128;
    public const int ActionMaxLength = 64;
    public const int TargetMaxLength = 200;
    public const int DetailsMaxLength = 500;

    private AuditEvent()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary><c>sub</c> do usuario no provedor de identidade, ou <see cref="SystemActor"/> (jobs, CLI e operacoes administrativas).</summary>
    public string ActorId { get; private set; } = SystemActor;

    public string Action { get; private set; } = string.Empty;

    public string? Target { get; private set; }

    /// <summary>Texto curto escrito pelo codigo. Nunca segredos nem dados pessoais de compradores.</summary>
    public string? Details { get; private set; }

    public static AuditEvent Create(Guid tenantId, string? actorId, string action, string? target, string? details, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return new AuditEvent
        {
            TenantId = tenantId,
            OccurredAt = now,
            ActorId = string.IsNullOrWhiteSpace(actorId) ? SystemActor : Truncate(actorId.Trim(), ActorMaxLength)!,
            Action = Truncate(action.Trim(), ActionMaxLength)!,
            Target = Truncate(target, TargetMaxLength),
            Details = Truncate(details, DetailsMaxLength),
        };
    }

    // Auditar nunca deve derrubar a operacao por tamanho de texto: o excesso e cortado.
    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }
}
