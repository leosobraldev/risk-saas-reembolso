using RiskGuard.Modules.Risk.Application.Rules;
using static RiskGuard.UnitTests.Risk.RiskTestData;

namespace RiskGuard.UnitTests.Risk;

public class RefundHistoryRuleTests
{
    private readonly RefundHistoryRule _rule = new();

    [Fact]
    public void Rule_name_is_stable()
    {
        _rule.Name.Should().Be("RefundHistoryRule");
    }

    [Fact]
    public void Customers_without_refunds_are_not_scored()
    {
        _rule.Evaluate(Context(refunds: 0)).Should().BeNull();
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    [InlineData(4, 40)]
    [InlineData(5, 40)] // teto
    [InlineData(10, 40)]
    [InlineData(int.MaxValue, 40)] // sem overflow
    public void Points_grow_ten_per_refund_up_to_a_cap_of_forty(int refunds, int expectedPoints)
    {
        var contribution = _rule.Evaluate(Context(refunds: refunds));

        contribution!.Points.Should().Be(expectedPoints);
        contribution.RuleName.Should().Be(nameof(RefundHistoryRule));
    }

    [Fact]
    public void Reason_mentions_the_refund_count()
    {
        _rule.Evaluate(Context(refunds: 3))!.Reason.Should().Contain("3");
    }

    [Fact]
    public void Negative_counts_are_treated_as_no_history()
    {
        _rule.Evaluate(Context(refunds: -1)).Should().BeNull();
    }
}
