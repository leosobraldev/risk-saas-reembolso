using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Core.Domain;
using RiskGuard.Modules.Risk.Application.Rules;

namespace RiskGuard.Modules.Risk.Domain;

/// <summary>Resultado imutavel da avaliacao de um pedido (um por pedido).</summary>
public sealed class RiskEvaluation : Entity, ITenantEntity
{
    private readonly List<RiskFactor> _factors = [];

    private RiskEvaluation()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    public Guid OrderId { get; private set; }

    // Copia do pedido para o dashboard: o RiskModule nao le o schema do IngestionModule.
    public string Platform { get; private set; } = string.Empty;

    public string ExternalOrderId { get; private set; } = string.Empty;

    public decimal TotalAmount { get; private set; }

    public int Score { get; private set; }

    public RiskLevel Level { get; private set; }

    public DateTimeOffset CalculatedAt { get; private set; }

    public IReadOnlyCollection<RiskFactor> Factors => _factors.AsReadOnly();

    public static RiskEvaluation Create(
        Guid tenantId,
        Guid orderId,
        string platform,
        string externalOrderId,
        decimal totalAmount,
        RiskAssessment assessment,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("OrderId nao pode ser vazio.", nameof(orderId));
        }

        ArgumentNullException.ThrowIfNull(assessment);

        var evaluation = new RiskEvaluation
        {
            TenantId = tenantId,
            OrderId = orderId,
            Platform = platform,
            ExternalOrderId = externalOrderId,
            TotalAmount = totalAmount,
            Score = assessment.Score,
            Level = assessment.Level,
            CalculatedAt = now,
        };

        evaluation._factors.AddRange(
            assessment.Contributions.Select(c => RiskFactor.Create(tenantId, evaluation.Id, c.Reason, c.Points)));

        return evaluation;
    }
}
