using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Risk.Application.Rules;
using RiskGuard.Modules.Risk.Domain;

namespace RiskGuard.UnitTests.Risk;

public class RiskDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluation_snapshots_the_assessment_and_its_factors()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var assessment = new RiskAssessment(
            60,
            RiskLevel.Medium,
            [new RiskContribution("SuspiciousEmailRule", "e-mail suspeito", 50), new RiskContribution("HighOrderValueRule", "valor alto", 10)]);

        var evaluation = RiskEvaluation.Create(tenantId, orderId, "shopify", "1001", 99.90m, assessment, Now);

        evaluation.TenantId.Should().Be(tenantId);
        evaluation.OrderId.Should().Be(orderId);
        evaluation.Platform.Should().Be("shopify");
        evaluation.ExternalOrderId.Should().Be("1001");
        evaluation.TotalAmount.Should().Be(99.90m);
        evaluation.Score.Should().Be(60);
        evaluation.Level.Should().Be(RiskLevel.Medium);
        evaluation.CalculatedAt.Should().Be(Now);
        evaluation.Factors.Should().HaveCount(2);
        evaluation.Factors.Should().OnlyContain(f => f.TenantId == tenantId && f.RiskEvaluationId == evaluation.Id);
        evaluation.Factors.Select(f => f.PointsAdded).Should().Equal(50, 10);
        evaluation.Factors.Select(f => f.Reason).Should().Equal("e-mail suspeito", "valor alto");
    }

    [Fact]
    public void Evaluation_without_factors_is_valid()
    {
        var evaluation = RiskEvaluation.Create(Guid.NewGuid(), Guid.NewGuid(), "shopify", "1", 1m, new RiskAssessment(0, RiskLevel.Low, []), Now);

        evaluation.Factors.Should().BeEmpty();
        evaluation.Score.Should().Be(0);
    }

    [Fact]
    public void Evaluation_rejects_empty_tenant_or_order()
    {
        var assessment = new RiskAssessment(0, RiskLevel.Low, []);

        ((Action)(() => RiskEvaluation.Create(Guid.Empty, Guid.NewGuid(), "p", "1", 1m, assessment, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => RiskEvaluation.Create(Guid.NewGuid(), Guid.Empty, "p", "1", 1m, assessment, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => RiskEvaluation.Create(Guid.NewGuid(), Guid.NewGuid(), "p", "1", 1m, null!, Now))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Customer_history_starts_without_refunds_and_counts_them()
    {
        var tenantId = Guid.NewGuid();

        var history = CustomerHistory.Create(tenantId, "  Cliente@Loja.COM ");
        history.RecordRefund();
        history.RecordRefund();

        history.TenantId.Should().Be(tenantId);
        history.CustomerEmail.Should().Be("cliente@loja.com");
        history.TotalRefunds.Should().Be(2);
    }

    [Fact]
    public void Customer_history_rejects_invalid_input()
    {
        ((Action)(() => CustomerHistory.Create(Guid.Empty, "a@b.com"))).Should().Throw<ArgumentException>();
        ((Action)(() => CustomerHistory.Create(Guid.NewGuid(), " "))).Should().Throw<ArgumentException>();
    }
}
