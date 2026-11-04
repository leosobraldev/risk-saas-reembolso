using RiskGuard.Core.Abstractions;

namespace RiskGuard.UnitTests.Core;

public class ResultTests
{
    private static readonly Error Failure = new("x.fail", "falhou", ErrorType.Conflict);

    [Fact]
    public void Success_exposes_value_and_hides_error()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        ((Action)(() => _ = result.Error)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_exposes_error_and_hides_value()
    {
        Result<int> result = Failure;

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(Failure);
        ((Action)(() => _ = result.Value)).Should().Throw<InvalidOperationException>();
    }
}
