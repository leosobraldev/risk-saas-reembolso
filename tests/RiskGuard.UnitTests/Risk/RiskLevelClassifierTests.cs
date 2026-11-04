using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Risk.Application.Rules;

namespace RiskGuard.UnitTests.Risk;

/// <summary>Regra: Alto quando score &gt; limite do lojista (SDD 6.3); Medio a partir da metade do limite; zero e sempre Baixo.</summary>
public class RiskLevelClassifierTests
{
    [Theory]
    [InlineData(81, 80, RiskLevel.High)]
    [InlineData(90, 80, RiskLevel.High)] // cenario 3 do SDD
    [InlineData(100, 80, RiskLevel.High)]
    [InlineData(80, 80, RiskLevel.Medium)] // igual ao limite NAO e Alto
    [InlineData(79, 80, RiskLevel.Medium)]
    [InlineData(40, 80, RiskLevel.Medium)] // metade do limite
    [InlineData(39, 80, RiskLevel.Low)]
    [InlineData(1, 80, RiskLevel.Low)]
    [InlineData(0, 80, RiskLevel.Low)]
    public void Default_threshold_of_eighty(int score, int threshold, RiskLevel expected)
    {
        RiskLevelClassifier.Classify(score, threshold).Should().Be(expected);
    }

    [Theory]
    [InlineData(41, 40, RiskLevel.High)]
    [InlineData(40, 40, RiskLevel.Medium)]
    [InlineData(20, 40, RiskLevel.Medium)]
    [InlineData(19, 40, RiskLevel.Low)]
    [InlineData(60, 60, RiskLevel.Medium)]
    [InlineData(61, 60, RiskLevel.High)]
    public void Custom_thresholds_move_both_boundaries(int score, int threshold, RiskLevel expected)
    {
        RiskLevelClassifier.Classify(score, threshold).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0, RiskLevel.Low)] // score zero nunca e Medio, mesmo com limite zero
    [InlineData(1, 0, RiskLevel.High)] // limite zero: qualquer pontuacao e Alta
    [InlineData(100, 100, RiskLevel.Medium)] // limite 100 desliga o nivel Alto
    [InlineData(50, 100, RiskLevel.Medium)]
    [InlineData(49, 100, RiskLevel.Low)]
    public void Extreme_thresholds_behave_predictably(int score, int threshold, RiskLevel expected)
    {
        RiskLevelClassifier.Classify(score, threshold).Should().Be(expected);
    }

    [Theory]
    [InlineData(-5, 80, RiskLevel.Low)]
    [InlineData(150, 80, RiskLevel.High)]
    [InlineData(50, -10, RiskLevel.High)] // limite negativo e tratado como 0
    [InlineData(50, 500, RiskLevel.Medium)] // limite > 100 e tratado como 100
    public void Out_of_range_inputs_are_clamped_not_thrown(int score, int threshold, RiskLevel expected)
    {
        RiskLevelClassifier.Classify(score, threshold).Should().Be(expected);
    }
}
