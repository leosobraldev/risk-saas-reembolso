using RiskGuard.Modules.Risk.Application.Rules;
using static RiskGuard.UnitTests.Risk.RiskTestData;

namespace RiskGuard.UnitTests.Risk;

public class HighOrderValueRuleTests
{
    private readonly HighOrderValueRule _rule = new();

    [Fact]
    public void Rule_name_is_stable()
    {
        _rule.Name.Should().Be("HighOrderValueRule");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4999.99)]
    public void Orders_below_the_first_tier_are_not_scored(double total)
    {
        _rule.Evaluate(Context(total: (decimal)total)).Should().BeNull();
    }

    [Theory]
    [InlineData(5000.00, 10)]
    [InlineData(7500.50, 10)]
    [InlineData(9999.99, 10)]
    [InlineData(10000.00, 20)]
    [InlineData(250000.00, 20)]
    public void Tiers_start_at_the_boundary_value(double total, int expectedPoints)
    {
        var contribution = _rule.Evaluate(Context(total: (decimal)total));

        contribution!.Points.Should().Be(expectedPoints);
        contribution.RuleName.Should().Be(nameof(HighOrderValueRule));
        contribution.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Decimal_max_does_not_overflow()
    {
        _rule.Evaluate(Context(total: decimal.MaxValue))!.Points.Should().Be(20);
    }

    [Fact]
    public void Negative_totals_are_not_scored()
    {
        _rule.Evaluate(Context(total: -10_000m)).Should().BeNull();
    }
}
