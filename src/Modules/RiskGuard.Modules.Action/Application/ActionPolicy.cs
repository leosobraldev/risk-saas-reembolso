using RiskGuard.Core.Contracts;

namespace RiskGuard.Modules.Action.Application;

public enum ActionDecision
{
    None,
    Block,
    RecommendOnly,
}

/// <summary>
/// Regra de decisao (SDD 6.3): risco Alto = score ESTRITAMENTE maior que o limite do lojista. Com o limite lido do
/// cache do tenant; so bloqueia se o lojista habilitou o bloqueio automatico. Sem tenant conhecido ou inativo: nada.
/// </summary>
public static class ActionPolicy
{
    public static ActionDecision Decide(int score, TenantSnapshot? tenant)
    {
        if (tenant is not { IsActive: true })
        {
            return ActionDecision.None;
        }

        var clampedScore = Math.Clamp(score, 0, 100);
        if (clampedScore <= tenant.HighRiskThreshold)
        {
            return ActionDecision.None;
        }

        return tenant.AutoBlockEnabled ? ActionDecision.Block : ActionDecision.RecommendOnly;
    }
}
