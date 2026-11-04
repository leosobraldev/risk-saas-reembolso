using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Risk.Domain;

/// <summary>Motivo individual que compoe o score. Carrega o TenantId para que a RLS proteja a tabela diretamente.</summary>
public sealed class RiskFactor : Entity, ITenantEntity
{
    public const int ReasonMaxLength = 500;

    private RiskFactor()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    public Guid RiskEvaluationId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public int PointsAdded { get; private set; }

    internal static RiskFactor Create(Guid tenantId, Guid evaluationId, string reason, int points) =>
        new()
        {
            TenantId = tenantId,
            RiskEvaluationId = evaluationId,
            Reason = reason.Length <= ReasonMaxLength ? reason : reason[..ReasonMaxLength],
            PointsAdded = points,
        };
}
