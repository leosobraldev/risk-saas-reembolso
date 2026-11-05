using RiskGuard.Modules.Ingestion.Application;
using RiskGuard.Modules.Ingestion.Application.Parsing;

namespace RiskGuard.UnitTests.Ingestion;

/// <summary>
/// Wake: o webhook (<c>pedido.novo</c>) traz so a loja e o numero do pedido; e-mail e valor vem de GET /pedidos/{id}. Os dois
/// formatos passam pelo mesmo parser: a notificacao pede a busca, e o pedido buscado vira o pedido normalizado.
/// </summary>
public class WakeWebhookParserTests
{
    private readonly WakeWebhookParser _parser = new();

    private const string NewOrderNotification = """
        {
          "topico": "pedido.novo",
          "evento": { "loja": "NomeLoja", "pedido": 123456789, "id": "0000aaaa-0aaa-0000-aa00-00aaaa0a00a0", "data_evento": "2026-03-01T09:11:51" },
          "data_envio": "2026-03-01T09:11:52.00",
          "tentativa": 1
        }
        """;

    [Fact]
    public void Platform_is_wake()
    {
        _parser.Platform.Should().Be("wake");
    }

    [Fact]
    public void Parses_the_order_fetched_from_the_api()
    {
        const string order = """
            {
              "pedidoId": 123456789,
              "situacaoPedidoId": 1,
              "data": "2026-03-01T09:11:51.877Z",
              "valorTotalPedido": 1234.5,
              "usuario": { "usuarioId": 9, "nome": "Ana", "email": "Ana@Exemplo.com.br" }
            }
            """;

        var result = _parser.Parse(order);

        result.IsSuccess.Should().BeTrue();
        result.Value.Platform.Should().Be("wake");
        result.Value.ExternalOrderId.Should().Be("123456789");
        result.Value.CustomerEmail.Should().Be("ana@exemplo.com.br");
        result.Value.TotalAmount.Should().Be(1234.50m);
        result.Value.CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 1, 9, 11, 51, 877, TimeSpan.Zero));
        result.Value.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void An_order_date_without_offset_is_read_as_utc()
    {
        const string order = """
            { "pedidoId": 1, "valorTotalPedido": 10, "data": "2026-03-01T09:11:51", "usuario": { "email": "a@b.com" } }
            """;

        _parser.Parse(order).Value.CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 1, 9, 11, 51, TimeSpan.Zero));
    }

    [Fact]
    public void A_new_order_notification_asks_for_the_order_to_be_fetched()
    {
        var result = _parser.Parse(NewOrderNotification);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(WebhookErrorCodes.NeedsOrderFetch);
    }

    [Theory]
    [InlineData("pedido.situacao.alterado")]
    [InlineData("pedido.rastreamento.alterado")]
    [InlineData("pedido.produto.rastreamento.alterado")]
    [InlineData("pedido.valido.alterado")]
    [InlineData("pedido.novo.indexado")]
    [InlineData("centro_distribuicao.novo")]
    [InlineData("usuario.novo")]
    public void Notifications_of_other_topics_are_ignored_not_failed(string topic)
    {
        var payload = $$"""{ "topico": "{{topic}}", "evento": { "loja": "NomeLoja", "pedido": 1 }, "tentativa": 1 }""";

        var result = _parser.Parse(payload);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(WebhookErrorCodes.Ignored);
    }

    [Fact]
    public void The_order_id_is_read_from_the_notification_event()
    {
        _parser.ReadNotificationOrderId(NewOrderNotification).Should().Be("123456789");
    }

    [Theory]
    [InlineData("""{ "topico": "pedido.novo", "evento": { "loja": "x" } }""")]
    [InlineData("""{ "topico": "pedido.novo" }""")]
    [InlineData("não é json")]
    [InlineData("[1,2]")]
    public void A_notification_without_a_readable_order_id_gives_none(string payload)
    {
        _parser.ReadNotificationOrderId(payload).Should().BeNull();
    }

    [Fact]
    public void A_new_order_notification_without_the_order_id_is_invalid_not_fetched()
    {
        const string payload = """{ "topico": "pedido.novo", "evento": { "loja": "NomeLoja" } }""";

        var result = _parser.Parse(payload);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("webhook.invalid_payload");
    }

    [Theory]
    [InlineData("""{ "pedidoId": 1, "valorTotalPedido": 10, "data": "2026-03-01T09:11:51Z", "usuario": {} }""")]
    [InlineData("""{ "pedidoId": 1, "valorTotalPedido": 10, "data": "2026-03-01T09:11:51Z", "usuario": { "email": "sem-arroba" } }""")]
    [InlineData("""{ "pedidoId": 1, "valorTotalPedido": -1, "data": "2026-03-01T09:11:51Z", "usuario": { "email": "a@b.com" } }""")]
    [InlineData("""{ "pedidoId": 1, "data": "2026-03-01T09:11:51Z", "usuario": { "email": "a@b.com" } }""")]
    [InlineData("""{ "valorTotalPedido": 10, "data": "2026-03-01T09:11:51Z", "usuario": { "email": "a@b.com" } }""")]
    [InlineData("""{ "pedidoId": 1, "valorTotalPedido": 10, "usuario": { "email": "a@b.com" } }""")]
    public void An_incomplete_fetched_order_is_invalid(string payload)
    {
        var result = _parser.Parse(payload);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("webhook.invalid_payload");
    }

    [Fact]
    public void A_json_that_is_neither_notification_nor_order_is_invalid()
    {
        var result = _parser.Parse("""{ "qualquer": "coisa" }""");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("webhook.invalid_payload");
    }
}
