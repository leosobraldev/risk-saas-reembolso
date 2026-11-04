using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Risk.Application.Rules;
using static RiskGuard.UnitTests.Risk.RiskTestData;

namespace RiskGuard.UnitTests.Risk;

/// <summary>Pipes and Filters: cada IRiskRule e um filtro independente; o pipeline so agrega.</summary>
public class RiskPipelineTests
{
    private const int Threshold = 80;

    [Fact]
    public void Without_rules_the_score_is_zero_and_level_low()
    {
        var assessment = new RiskPipeline([]).Evaluate(Context(), Threshold);

        assessment.Score.Should().Be(0);
        assessment.Level.Should().Be(RiskLevel.Low);
        assessment.Contributions.Should().BeEmpty();
    }

    [Fact]
    public void Scores_are_summed_across_independent_rules_and_every_factor_is_kept()
    {
        var pipeline = new RiskPipeline([Rule("A", 30), Rule("B", 15), Rule("C", 5)]);

        var assessment = pipeline.Evaluate(Context(), Threshold);

        assessment.Score.Should().Be(50);
        assessment.Contributions.Select(c => c.RuleName).Should().Equal("A", "B", "C");
        assessment.Contributions.Sum(c => c.Points).Should().Be(50);
    }

    [Fact]
    public void Rules_that_do_not_apply_are_skipped()
    {
        var pipeline = new RiskPipeline([Rule("A", 20), NotApplicable("B"), Rule("C", 10)]);

        var assessment = pipeline.Evaluate(Context(), Threshold);

        assessment.Score.Should().Be(30);
        assessment.Contributions.Select(c => c.RuleName).Should().Equal("A", "C");
    }

    [Fact]
    public void Score_is_capped_at_one_hundred()
    {
        var pipeline = new RiskPipeline([Rule("A", 60), Rule("B", 60)]);

        var assessment = pipeline.Evaluate(Context(), Threshold);

        assessment.Score.Should().Be(100);
        assessment.Contributions.Sum(c => c.Points).Should().Be(120); // auditoria preserva o bruto
    }

    [Fact]
    public void Rules_run_in_registration_order_and_each_runs_exactly_once()
    {
        var calls = new List<string>();
        var pipeline = new RiskPipeline([Tracking("first", calls), Tracking("second", calls), Tracking("third", calls)]);

        pipeline.Evaluate(Context(), Threshold);

        calls.Should().Equal("first", "second", "third");
    }

    [Fact]
    public void Every_rule_receives_the_same_context_instance()
    {
        var context = Context();
        var a = Substitute.For<IRiskRule>();
        var b = Substitute.For<IRiskRule>();
        a.Name.Returns("A");
        b.Name.Returns("B");

        new RiskPipeline([a, b]).Evaluate(context, Threshold);

        a.Received(1).Evaluate(context);
        b.Received(1).Evaluate(context);
    }

    [Fact]
    public void A_throwing_rule_fails_the_evaluation_instead_of_silently_under_scoring()
    {
        var broken = Substitute.For<IRiskRule>();
        broken.Name.Returns("Broken");
        broken.Evaluate(Arg.Any<RiskContext>()).Returns(_ => throw new InvalidOperationException("boom"));
        var pipeline = new RiskPipeline([Rule("A", 10), broken]);

        var act = () => pipeline.Evaluate(Context(), Threshold);

        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public void Tenant_threshold_drives_the_level_of_the_same_score()
    {
        var pipeline = new RiskPipeline([Rule("A", 50)]);

        pipeline.Evaluate(Context(), highRiskThreshold: 80).Level.Should().Be(RiskLevel.Medium);
        pipeline.Evaluate(Context(), highRiskThreshold: 40).Level.Should().Be(RiskLevel.High);
        pipeline.Evaluate(Context(), highRiskThreshold: 100).Level.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void Rule_contributions_must_be_positive_and_within_range()
    {
        var negative = () => new RiskContribution("R", "reason", -1);
        var zero = () => new RiskContribution("R", "reason", 0);
        var tooMuch = () => new RiskContribution("R", "reason", 101);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        zero.Should().Throw<ArgumentOutOfRangeException>();
        tooMuch.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var pipeline = new RiskPipeline([]);

        ((Action)(() => pipeline.Evaluate(null!, Threshold))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new RiskPipeline(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Default_registration_flags_the_acceptance_scenario_email_with_50_points()
    {
        var pipeline = new RiskPipeline([new SuspiciousEmailRule(), new RefundHistoryRule(), new HighOrderValueRule()]);

        var assessment = pipeline.Evaluate(Context(email: "fraude@teste.com"), Threshold);

        assessment.Score.Should().Be(50);
        assessment.Contributions.Should().ContainSingle(c => c.RuleName == nameof(SuspiciousEmailRule) && c.Points == 50);
    }

    [Fact]
    public void All_default_rules_combined_reach_a_high_score_for_a_very_risky_order()
    {
        var pipeline = new RiskPipeline([new SuspiciousEmailRule(), new RefundHistoryRule(), new HighOrderValueRule()]);

        var assessment = pipeline.Evaluate(Context(email: "fraude@teste.com", refunds: 4, total: 12_000m), Threshold);

        assessment.Score.Should().Be(100); // 50 + 40 + 20 = 110 -> teto
        assessment.Level.Should().Be(RiskLevel.High);
    }

    private static IRiskRule Rule(string name, int points)
    {
        var rule = Substitute.For<IRiskRule>();
        rule.Name.Returns(name);
        rule.Evaluate(Arg.Any<RiskContext>()).Returns(new RiskContribution(name, $"{name} reason", points));
        return rule;
    }

    private static IRiskRule NotApplicable(string name)
    {
        var rule = Substitute.For<IRiskRule>();
        rule.Name.Returns(name);
        rule.Evaluate(Arg.Any<RiskContext>()).Returns((RiskContribution?)null);
        return rule;
    }

    private static IRiskRule Tracking(string name, List<string> calls)
    {
        var rule = Substitute.For<IRiskRule>();
        rule.Name.Returns(name);
        rule.Evaluate(Arg.Any<RiskContext>()).Returns(_ =>
        {
            calls.Add(name);
            return null;
        });
        return rule;
    }
}
