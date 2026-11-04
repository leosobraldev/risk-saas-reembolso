using RiskGuard.Modules.Ingestion.Application.Parsing;

namespace RiskGuard.UnitTests.Ingestion;

public class ShopifyWebhookParserTests
{
    private readonly ShopifyWebhookParser _parser = new();

    [Fact]
    public void Platform_is_shopify()
    {
        _parser.Platform.Should().Be("shopify");
    }

    [Fact]
    public void Parses_a_standard_orders_create_payload()
    {
        const string payload = """
            {
              "id": 820982911946154508,
              "email": "  Fraude@Teste.com ",
              "created_at": "2026-03-01T10:15:30-03:00",
              "total_price": "403.50",
              "currency": "BRL"
            }
            """;

        var result = _parser.Parse(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExternalOrderId.Should().Be("820982911946154508");
        result.Value.CustomerEmail.Should().Be("fraude@teste.com");
        result.Value.TotalAmount.Should().Be(403.50m);
        result.Value.CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 1, 10, 15, 30, TimeSpan.FromHours(-3)));
    }

    [Fact]
    public void Falls_back_to_customer_email_when_root_email_is_missing()
    {
        const string payload = """
            { "id": 1, "created_at": "2026-03-01T10:15:30Z", "total_price": "10.00", "customer": { "email": "c@x.com" } }
            """;

        _parser.Parse(payload).Value.CustomerEmail.Should().Be("c@x.com");
    }

    [Fact]
    public void Accepts_numeric_total_price()
    {
        const string payload = """
            { "id": 1, "email": "a@b.com", "created_at": "2026-03-01T10:15:30Z", "total_price": 99.9 }
            """;

        _parser.Parse(payload).Value.TotalAmount.Should().Be(99.9m);
    }

    [Theory]
    [InlineData("""{ "email": "a@b.com", "created_at": "2026-03-01T10:15:30Z", "total_price": "1" }""", "id")]
    [InlineData("""{ "id": 1, "created_at": "2026-03-01T10:15:30Z", "total_price": "1" }""", "email")]
    [InlineData("""{ "id": 1, "email": "a@b.com", "created_at": "2026-03-01T10:15:30Z" }""", "total")]
    [InlineData("""{ "id": 1, "email": "a@b.com", "total_price": "1" }""", "created_at")]
    public void Missing_required_field_is_a_validation_error_naming_the_field(string payload, string field)
    {
        var result = _parser.Parse(payload);

        result.IsSuccess.Should().BeFalse();
        result.Error.Message.Should().Contain(field);
    }

    [Theory]
    [InlineData("""{ "id": 1, "email": "a@b.com", "created_at": "2026-03-01T10:15:30Z", "total_price": "abc" }""")]
    [InlineData("""{ "id": 1, "email": "a@b.com", "created_at": "2026-03-01T10:15:30Z", "total_price": "-5.00" }""")]
    [InlineData("""{ "id": 1, "email": "not-an-email", "created_at": "2026-03-01T10:15:30Z", "total_price": "1" }""")]
    [InlineData("""{ "id": 1, "email": "a@b.com", "created_at": "yesterday", "total_price": "1" }""")]
    public void Malformed_values_are_rejected(string payload)
    {
        _parser.Parse(payload).IsSuccess.Should().BeFalse();
    }
}
