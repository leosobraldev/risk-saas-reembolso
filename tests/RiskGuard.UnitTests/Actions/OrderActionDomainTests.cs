using RiskGuard.Modules.Action.Domain;

namespace RiskGuard.UnitTests.Actions;

public class OrderActionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static OrderAction NewBlock() =>
        OrderAction.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "shopify", "1001", 90, OrderActionType.Block, Now);

    [Fact]
    public void Block_starts_pending_with_the_evaluation_snapshot()
    {
        var tenantId = Guid.NewGuid();
        var evaluationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var action = OrderAction.CreatePending(tenantId, evaluationId, orderId, "shopify", "1001", 90, OrderActionType.Block, Now);

        action.TenantId.Should().Be(tenantId);
        action.EvaluationId.Should().Be(evaluationId);
        action.OrderId.Should().Be(orderId);
        action.Platform.Should().Be("shopify");
        action.ExternalOrderId.Should().Be("1001");
        action.Score.Should().Be(90);
        action.Type.Should().Be(OrderActionType.Block);
        action.Status.Should().Be(OrderActionStatus.Pending);
        action.IsPending.Should().BeTrue();
        action.Attempts.Should().Be(0);
        action.CreatedAt.Should().Be(Now);
        action.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Skipped_is_final_and_keeps_the_reason()
    {
        var action = OrderAction.CreateSkipped(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "shopify", "1", 95, OrderActionType.Block, "auto-block desligado", Now);

        action.Status.Should().Be(OrderActionStatus.Skipped);
        action.IsPending.Should().BeFalse();
        action.LastError.Should().Be("auto-block desligado");
        action.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void MarkSucceeded_completes_the_action_and_clears_the_error()
    {
        var action = NewBlock();
        action.RecordFailedAttempt("timeout", maxRetries: 5, Now);

        action.MarkSucceeded(Now.AddMinutes(1));

        action.Status.Should().Be(OrderActionStatus.Succeeded);
        action.CompletedAt.Should().Be(Now.AddMinutes(1));
        action.LastError.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_is_a_permanent_failure_with_the_reason()
    {
        var action = NewBlock();

        action.MarkFailed("pedido nao encontrado", Now);

        action.Status.Should().Be(OrderActionStatus.Failed);
        action.LastError.Should().Be("pedido nao encontrado");
        action.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void Transient_failures_stay_pending_until_the_retries_are_exhausted()
    {
        var action = NewBlock();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            action.RecordFailedAttempt($"erro {attempt}", maxRetries: 3, Now);
            action.Status.Should().Be(OrderActionStatus.Pending);
            action.Attempts.Should().Be(attempt);
        }

        action.RecordFailedAttempt("erro 4", maxRetries: 3, Now); // 1 execucao + 3 retries = 4a falha encerra

        action.Status.Should().Be(OrderActionStatus.Failed);
        action.Attempts.Should().Be(4);
        action.LastError.Should().Be("erro 4");
        action.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void Zero_retries_fail_on_the_first_failed_attempt()
    {
        var action = NewBlock();

        action.RecordFailedAttempt("x", maxRetries: 0, Now);

        action.Status.Should().Be(OrderActionStatus.Failed);
    }

    [Fact]
    public void Long_errors_are_truncated()
    {
        var action = NewBlock();

        action.MarkFailed(new string('x', OrderAction.ErrorMaxLength + 500), Now);

        action.LastError!.Length.Should().Be(OrderAction.ErrorMaxLength);
    }

    [Fact]
    public void Finished_actions_cannot_change_state_again()
    {
        var action = NewBlock();
        action.MarkSucceeded(Now);

        ((Action)(() => action.MarkFailed("x", Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => action.MarkSucceeded(Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => action.RecordFailedAttempt("x", 1, Now))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Flag_actions_keep_their_type()
    {
        var action = OrderAction.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "nuvemshop", "1", 90, OrderActionType.Flag, Now);

        action.Type.Should().Be(OrderActionType.Flag);
    }

    // ---- liberacao ----

    private static OrderAction Succeeded()
    {
        var action = NewBlock();
        action.MarkSucceeded(Now);
        return action;
    }

    [Fact]
    public void A_successful_action_can_be_released_and_the_release_completes()
    {
        var action = Succeeded();

        action.RequestRelease().Should().Be(ReleaseRequestOutcome.Started);
        action.Status.Should().Be(OrderActionStatus.Releasing);
        action.IsReleasing.Should().BeTrue();
        action.CompletedAt.Should().BeNull();

        action.MarkReleased(Now.AddMinutes(5));

        action.Status.Should().Be(OrderActionStatus.Released);
        action.CompletedAt.Should().Be(Now.AddMinutes(5));
        action.LastError.Should().BeNull();
    }

    [Fact]
    public void Requesting_a_release_twice_does_not_start_a_second_one()
    {
        var action = Succeeded();
        action.RequestRelease();

        action.RequestRelease().Should().Be(ReleaseRequestOutcome.AlreadyReleasing);
    }

    [Theory]
    [InlineData(OrderActionStatus.Pending)]
    [InlineData(OrderActionStatus.Failed)]
    [InlineData(OrderActionStatus.Skipped)]
    [InlineData(OrderActionStatus.Released)]
    public void Only_applied_actions_are_releasable(OrderActionStatus status)
    {
        var action = status switch
        {
            OrderActionStatus.Pending => NewBlock(),
            OrderActionStatus.Failed => FailedAction(),
            OrderActionStatus.Skipped => OrderAction.CreateSkipped(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "shopify", "1", 95, OrderActionType.Block, "x", Now),
            _ => ReleasedAction(),
        };

        action.RequestRelease().Should().Be(ReleaseRequestOutcome.NotAllowed);
        action.Status.Should().Be(status);
    }

    [Fact]
    public void A_failed_release_can_be_requested_again_with_a_fresh_retry_budget()
    {
        var action = Succeeded();
        action.RequestRelease();
        action.RecordFailedReleaseAttempt("e1", maxRetries: 0, Now); // esgota: ReleaseFailed
        action.Status.Should().Be(OrderActionStatus.ReleaseFailed);

        action.RequestRelease().Should().Be(ReleaseRequestOutcome.Started);

        action.Status.Should().Be(OrderActionStatus.Releasing);
        action.Attempts.Should().Be(0);
        action.LastError.Should().BeNull();
    }

    [Fact]
    public void Transient_release_failures_retry_until_exhausted_then_mark_release_failed()
    {
        var action = Succeeded();
        action.RequestRelease();

        action.RecordFailedReleaseAttempt("a", maxRetries: 2, Now);
        action.RecordFailedReleaseAttempt("b", maxRetries: 2, Now);
        action.Status.Should().Be(OrderActionStatus.Releasing);

        action.RecordFailedReleaseAttempt("c", maxRetries: 2, Now);

        action.Status.Should().Be(OrderActionStatus.ReleaseFailed);
        action.Attempts.Should().Be(3);
        action.LastError.Should().Be("c");
    }

    [Fact]
    public void A_permanent_release_failure_records_the_reason()
    {
        var action = Succeeded();
        action.RequestRelease();

        action.MarkReleaseFailed("holds de outra origem", Now);

        action.Status.Should().Be(OrderActionStatus.ReleaseFailed);
        action.LastError.Should().Be("holds de outra origem");
    }

    [Fact]
    public void Release_transitions_are_only_valid_while_releasing()
    {
        var action = Succeeded();

        ((Action)(() => action.MarkReleased(Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => action.MarkReleaseFailed("x", Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => action.RecordFailedReleaseAttempt("x", 1, Now))).Should().Throw<InvalidOperationException>();
    }

    private static OrderAction FailedAction()
    {
        var action = NewBlock();
        action.MarkFailed("x", Now);
        return action;
    }

    private static OrderAction ReleasedAction()
    {
        var action = Succeeded();
        action.RequestRelease();
        action.MarkReleased(Now);
        return action;
    }

    [Fact]
    public void Creation_rejects_empty_ids_and_invalid_scores()
    {
        ((Action)(() => OrderAction.CreatePending(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "p", "1", 90, OrderActionType.Block, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => OrderAction.CreatePending(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "p", "1", 90, OrderActionType.Block, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => OrderAction.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", "1", 90, OrderActionType.Block, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => OrderAction.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "p", "", 90, OrderActionType.Block, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => OrderAction.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "p", "1", 101, OrderActionType.Block, Now))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => OrderAction.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "p", "1", -1, OrderActionType.Block, Now))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
