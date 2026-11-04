using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Action.Application;

namespace RiskGuard.UnitTests.Actions;

/// <summary>Criterio de aceite 3 (SDD 6): score 90, limite do lojista 80 (lido do cache) => bloquear.</summary>
public class ActionPolicyTests
{
    private static TenantSnapshot Tenant(int threshold = 80, bool autoBlock = true, bool active = true) =>
        new(Guid.NewGuid(), active, threshold, autoBlock);

    [Fact]
    public void Acceptance_scenario_score_90_over_threshold_80_with_autoblock_blocks()
    {
        ActionPolicy.Decide(90, Tenant(threshold: 80, autoBlock: true)).Should().Be(ActionDecision.Block);
    }

    [Theory]
    [InlineData(81, 80)]
    [InlineData(100, 80)]
    [InlineData(51, 50)]
    [InlineData(1, 0)]
    [InlineData(41, 40)]
    public void Scores_strictly_above_the_threshold_block(int score, int threshold)
    {
        ActionPolicy.Decide(score, Tenant(threshold)).Should().Be(ActionDecision.Block);
    }

    [Theory]
    [InlineData(80, 80)] // igual ao limite NAO e risco alto
    [InlineData(79, 80)]
    [InlineData(0, 80)]
    [InlineData(50, 50)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    public void Scores_at_or_below_the_threshold_do_nothing(int score, int threshold)
    {
        ActionPolicy.Decide(score, Tenant(threshold)).Should().Be(ActionDecision.None);
    }

    [Theory]
    [InlineData(81)]
    [InlineData(90)]
    [InlineData(100)]
    public void High_risk_without_autoblock_is_recorded_as_recommendation_only(int score)
    {
        ActionPolicy.Decide(score, Tenant(autoBlock: false)).Should().Be(ActionDecision.RecommendOnly);
    }

    [Fact]
    public void Below_threshold_without_autoblock_does_nothing()
    {
        ActionPolicy.Decide(50, Tenant(autoBlock: false)).Should().Be(ActionDecision.None);
    }

    [Fact]
    public void Inactive_tenants_are_never_acted_upon()
    {
        ActionPolicy.Decide(100, Tenant(active: false)).Should().Be(ActionDecision.None);
        ActionPolicy.Decide(100, Tenant(autoBlock: false, active: false)).Should().Be(ActionDecision.None);
    }

    [Fact]
    public void Unknown_tenant_never_triggers_an_external_call()
    {
        ActionPolicy.Decide(100, null).Should().Be(ActionDecision.None);
    }

    [Theory]
    [InlineData(-10, 80)]
    [InlineData(500, 80)]
    public void Out_of_range_scores_are_clamped(int score, int threshold)
    {
        var expected = score > 100 ? ActionDecision.Block : ActionDecision.None;

        ActionPolicy.Decide(score, Tenant(threshold)).Should().Be(expected);
    }
}
