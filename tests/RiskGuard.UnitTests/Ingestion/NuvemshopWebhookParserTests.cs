using RiskGuard.Modules.Ingestion.Application.Parsing;

namespace RiskGuard.UnitTests.Ingestion;

public class NuvemshopWebhookParserTests
{
    private readonly NuvemshopWebhookParser _parser = new();

    [Fact]
    public void Platform_is_nuvemshop()
    {
        _parser.Platform.Should().Be("nuvemshop");
    }

    [Fact]
    public void Parses_an_order_resource_with_offset_without_colon()
    {
        const string payload = """
            {
              "id": 4501,
              "contact_email": "Comprador@Loja.com.br",
              "total": "250.00",
              "created_at": "2026-03-01T09:11:51+0000"
            }
            """;

        var result = _parser.Parse(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExternalOrderId.Should().Be("4501");
        result.Value.CustomerEmail.Should().Be("comprador@loja.com.br");
        result.Value.TotalAmount.Should().Be(250.00m);
        result.Value.CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 1, 9, 11, 51, TimeSpan.Zero));
    }

    [Fact]
    public void Accepts_standard_iso_offsets_too()
    {
        const string payload = """
            { "id": 1, "contact_email": "a@b.com", "total": 10, "created_at": "2026-03-01T09:11:51-03:00" }
            """;

        var createdAt = _parser.Parse(payload).Value.CreatedAt;

        createdAt.Should().Be(new DateTimeOffset(2026, 3, 1, 12, 11, 51, TimeSpan.Zero));
        createdAt.Offset.Should().Be(TimeSpan.Zero); // sempre UTC: PostgreSQL timestamptz exige offset 0
    }

    [Fact]
    public void Falls_back_to_customer_email()
    {
        const string payload = """
            { "id": 1, "total": "1.00", "created_at": "2026-03-01T09:11:51+0000", "customer": { "email": "c@x.com" } }
            """;

        _parser.Parse(payload).Value.CustomerEmail.Should().Be("c@x.com");
    }

    [Fact]
    public void Id_only_notification_asks_for_the_order_to_be_fetched()
    {
        // Webhooks reais da Nuvemshop trazem so { store_id, event, id }: o pedido e buscado na API da loja.
        const string payload = """{ "store_id": 1, "event": "order/created", "id": 4501 }""";

        var result = _parser.Parse(payload);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("webhook.needs_order_fetch");
    }
}
