using System.Text.Json;
using RiskGuard.Modules.Ingestion.Application.Parsing;

namespace RiskGuard.UnitTests.Ingestion;

/// <summary>Garante o esqueleto do Template Method: a base orquestra e valida; subclasses so leem campos.</summary>
public class WebhookParserBaseTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    public void Invalid_json_never_reaches_the_hooks(string payload)
    {
        var parser = new RecordingParser();

        var result = parser.Parse(payload);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("webhook.invalid_payload");
        parser.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void Non_object_root_is_rejected(string payload)
    {
        var parser = new RecordingParser();

        parser.Parse(payload).IsSuccess.Should().BeFalse();
        parser.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Hooks_run_once_each_in_a_fixed_order()
    {
        var parser = new RecordingParser();

        parser.Parse("{}");

        parser.Calls.Should().Equal("id", "email", "total", "created");
    }

    [Fact]
    public void Base_normalizes_and_rounds_whatever_the_subclass_returns()
    {
        var parser = new RecordingParser { Email = "  MIXED@Case.COM ", Total = 10.005m, Id = " 77 " };

        var result = parser.Parse("{}");

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("fake");
        result.Value.ExternalOrderId.Should().Be("77");
        result.Value.CustomerEmail.Should().Be("mixed@case.com");
        result.Value.TotalAmount.Should().Be(10.00m); // arredondamento bancario a 2 casas
    }

    [Fact]
    public void Created_at_is_always_normalized_to_utc()
    {
        var parser = new RecordingParser { CreatedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(-3)) };

        var created = parser.Parse("{}").Value.CreatedAt;

        created.Offset.Should().Be(TimeSpan.Zero);
        created.Should().Be(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Overlong_external_id_is_rejected()
    {
        var parser = new RecordingParser { Id = new string('9', 201) };

        parser.Parse("{}").IsSuccess.Should().BeFalse();
    }

    private sealed class RecordingParser : WebhookParserBase
    {
        public List<string> Calls { get; } = [];

        public string? Id { get; init; } = "1";

        public string? Email { get; init; } = "a@b.com";

        public decimal? Total { get; init; } = 1m;

        public DateTimeOffset CreatedAt { get; init; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override string Platform => "fake";

        protected override string? ReadExternalOrderId(JsonElement root)
        {
            Calls.Add("id");
            return Id;
        }

        protected override string? ReadCustomerEmail(JsonElement root)
        {
            Calls.Add("email");
            return Email;
        }

        protected override decimal? ReadTotalAmount(JsonElement root)
        {
            Calls.Add("total");
            return Total;
        }

        protected override DateTimeOffset? ReadCreatedAt(JsonElement root)
        {
            Calls.Add("created");
            return CreatedAt;
        }
    }
}
