using System.ComponentModel.DataAnnotations;
using RiskGuard.Infrastructure.Options;

namespace RiskGuard.UnitTests.Persistence;

public class RetentionOptionsTests
{
    private static bool IsValid(RetentionOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);

    [Fact]
    public void Defaults_are_valid_and_keep_audit_for_a_year()
    {
        var options = new RetentionOptions();

        IsValid(options).Should().BeTrue();
        options.AuditDays.Should().Be(365);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(179)] // abaixo do minimo imposto tambem pela RLS: a aplicacao nem sobe
    [InlineData(3651)]
    public void Audit_retention_below_the_floor_or_absurdly_long_is_rejected(int days)
    {
        IsValid(new RetentionOptions { AuditDays = days }).Should().BeFalse();
    }

    [Theory]
    [InlineData(180)]
    [InlineData(365)]
    [InlineData(3650)]
    public void Audit_retention_inside_the_range_is_accepted(int days)
    {
        IsValid(new RetentionOptions { AuditDays = days }).Should().BeTrue();
    }
}
