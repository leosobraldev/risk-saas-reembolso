using RiskGuard.Core.Contracts;

namespace RiskGuard.Modules.Risk.Application.Rules;

/// <summary>Tudo que as regras precisam saber do pedido. Imutavel e sem I/O: regras sao funcoes puras.</summary>
public sealed record RiskContext(
    Guid TenantId,
    Guid OrderId,
    string Platform,
    string ExternalOrderId,
    string CustomerEmail,
    decimal TotalAmount,
    DateTimeOffset OrderCreatedAt,
    int PreviousRefunds);

/// <summary>Pontos que uma regra adiciona ao score, com o motivo legivel (auditoria/dashboard).</summary>
public sealed record RiskContribution
{
    public const int MaxPoints = 100;

    public RiskContribution(string ruleName, string reason, int points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfLessThan(points, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(points, MaxPoints);

        RuleName = ruleName;
        Reason = reason;
        Points = points;
    }

    public string RuleName { get; }

    public string Reason { get; }

    public int Points { get; }
}

public sealed record RiskAssessment(int Score, RiskLevel Level, IReadOnlyList<RiskContribution> Contributions);

/// <summary>
/// Filtro do pipeline (Pipes and Filters). Cada regra e independente das demais: recebe o contexto e devolve
/// sua contribuicao ou <c>null</c> quando nao se aplica. Para criar uma nova regra, implemente e registre no modulo.
/// </summary>
public interface IRiskRule
{
    string Name { get; }

    RiskContribution? Evaluate(RiskContext context);
}
