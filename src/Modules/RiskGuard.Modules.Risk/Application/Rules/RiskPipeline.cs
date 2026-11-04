using RiskGuard.Core.Contracts;

namespace RiskGuard.Modules.Risk.Application.Rules;

/// <summary>Executa as regras em ordem de registro, soma os pontos (teto 100) e classifica o nivel.</summary>
public sealed class RiskPipeline
{
    public const int MaxScore = 100;

    private readonly IReadOnlyList<IRiskRule> _rules;

    public RiskPipeline(IEnumerable<IRiskRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToList();
    }

    public RiskAssessment Evaluate(RiskContext context, int highRiskThreshold)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Uma regra que lanca excecao interrompe a avaliacao: melhor o retry/alerta do que um score subestimado em silencio.
        var contributions = _rules
            .Select(rule => rule.Evaluate(context))
            .OfType<RiskContribution>()
            .ToList();

        var score = Math.Min(MaxScore, contributions.Sum(c => c.Points));
        return new RiskAssessment(score, RiskLevelClassifier.Classify(score, highRiskThreshold), contributions);
    }
}

public static class RiskLevelClassifier
{
    /// <summary>
    /// Alto: score &gt; limite do lojista. Medio: score &gt;= metade do limite. Baixo: o restante (inclui 0).
    /// Valores fora de 0..100 sao ajustados em vez de lancar excecao.
    /// </summary>
    public static RiskLevel Classify(int score, int highRiskThreshold)
    {
        var s = Math.Clamp(score, 0, RiskPipeline.MaxScore);
        var high = Math.Clamp(highRiskThreshold, 0, RiskPipeline.MaxScore);

        if (s > high)
        {
            return RiskLevel.High;
        }

        return s > 0 && s >= high / 2 ? RiskLevel.Medium : RiskLevel.Low;
    }
}
