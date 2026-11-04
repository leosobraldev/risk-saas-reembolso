using System.Net;
using System.Text.Json;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Modules.Action.Application.Blockers;
using RiskGuard.Modules.Action.Domain;
using static RiskGuard.UnitTests.Platforms.HttpTestKit;

namespace RiskGuard.UnitTests.Platforms;

/// <summary>Contrato verificado na documentacao da Shopify Admin REST: fulfillment_orders -> hold.json / release_hold.json.</summary>
public class ShopifyOrderBlockerTests
{
    private const string Shop = "loja-teste.myshopify.com";

    private static BlockRequest Request(string orderId = "820982911946154508", string? token = "shpat_token", string shop = Shop, string reason = "score de risco 90.") =>
        new(Guid.NewGuid(), orderId, 90, reason, Credentials("shopify", shop, token));

    private static string FulfillmentOrders(params (long Id, string Status, string[]? Holds)[] items) =>
        JsonSerializer.Serialize(new
        {
            fulfillment_orders = items.Select(i => new
            {
                id = i.Id,
                status = i.Status,
                fulfillment_holds = (i.Holds ?? []).Select(n => new { reason = "other", reason_notes = n }),
            }),
        });

    private static ShopifyOrderBlocker Blocker(FakeHttpHandler handler, string? baseUrl = null, string? version = null) =>
        new(Factory(handler), Options(baseUrl is null && version is null ? null : "shopify", baseUrl, version));

    [Fact]
    public void Shopify_can_really_pause_orders()
    {
        var blocker = Blocker(new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK)));

        blocker.Platform.Should().Be("shopify");
        blocker.ActionType.Should().Be(OrderActionType.Block);
    }

    [Fact]
    public async Task Block_lists_the_fulfillment_orders_then_places_each_open_one_on_hold()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((111, "open", null), (222, "scheduled", null), (333, "closed", null)))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await Blocker(handler).BlockAsync(Request());

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(3);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Uri.AbsoluteUri.Should().Be($"https://{Shop}/admin/api/{ShopifyOrderBlocker.DefaultApiVersion}/orders/820982911946154508/fulfillment_orders.json");
        handler.Requests.Skip(1).Select(r => r.Uri.AbsolutePath).Should().Equal(
            $"/admin/api/{ShopifyOrderBlocker.DefaultApiVersion}/fulfillment_orders/111/hold.json",
            $"/admin/api/{ShopifyOrderBlocker.DefaultApiVersion}/fulfillment_orders/222/hold.json"); // a closed e ignorada
        handler.Requests.Should().OnlyContain(r => r.Headers["X-Shopify-Access-Token"] == "shpat_token");
    }

    [Fact]
    public async Task Hold_body_follows_the_documented_shape_with_the_fraud_reason()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((111, "open", null)))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        await Blocker(handler).BlockAsync(Request(reason: "score de risco 90."));

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        var hold = body.RootElement.GetProperty("fulfillment_hold");
        hold.GetProperty("reason").GetString().Should().Be("high_risk_of_fraud");
        hold.GetProperty("reason_notes").GetString().Should().Be("RiskGuard: score de risco 90.");
        hold.GetProperty("notify_merchant").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Reason_notes_are_truncated_to_250_characters()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "open", null)))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        await Blocker(handler).BlockAsync(Request(reason: new string('x', 1000)));

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("fulfillment_hold").GetProperty("reason_notes").GetString()!.Length.Should().Be(250);
    }

    [Fact]
    public async Task Already_on_hold_is_success_without_posting_again()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "on_hold", ["RiskGuard: x"]))));

        var result = await Blocker(handler).BlockAsync(Request());

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().ContainSingle(); // so a listagem
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cancelled")]
    [InlineData("in_progress")]
    public async Task Orders_that_can_no_longer_be_paused_fail_permanently(string status)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, status, null))));

        var result = await Blocker(handler).BlockAsync(Request());

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        result.Error.Should().Contain(status);
    }

    [Fact]
    public async Task No_accessible_fulfillment_orders_fails_permanently()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, """{ "fulfillment_orders": [] }"""));

        var result = await Blocker(handler).BlockAsync(Request());

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, ExternalApiStatus.TransientFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, ExternalApiStatus.TransientFailure)]
    [InlineData(HttpStatusCode.NotFound, ExternalApiStatus.PermanentFailure)]
    [InlineData(HttpStatusCode.Unauthorized, ExternalApiStatus.PermanentFailure)]
    public async Task Listing_failures_are_classified(HttpStatusCode status, ExternalApiStatus expected)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(status));

        var result = await Blocker(handler).BlockAsync(Request());

        result.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, ExternalApiStatus.TransientFailure)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ExternalApiStatus.PermanentFailure)]
    public async Task Hold_failures_are_classified(HttpStatusCode status, ExternalApiStatus expected)
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "open", null)))
            : FakeHttpHandler.Status(status));

        var result = await Blocker(handler).BlockAsync(Request());

        result.Status.Should().Be(expected);
    }

    [Fact]
    public async Task Failure_on_a_later_fulfillment_order_stops_and_is_transient_so_the_retry_resumes_from_state()
    {
        var holds = 0;
        var handler = new FakeHttpHandler(r =>
        {
            if (r.Method == HttpMethod.Get)
            {
                return FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "open", null), (2, "open", null)));
            }

            return ++holds == 1 ? FakeHttpHandler.Json(HttpStatusCode.OK, "{}") : FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable);
        });

        var result = await Blocker(handler).BlockAsync(Request());

        result.Status.Should().Be(ExternalApiStatus.TransientFailure);
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Malformed_json_from_shopify_fails_permanently()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, "not json"));

        (await Blocker(handler).BlockAsync(Request())).Status.Should().Be(ExternalApiStatus.PermanentFailure);
    }

    [Fact]
    public async Task Network_errors_are_transient()
    {
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await Blocker(handler).BlockAsync(Request());

        result.Status.Should().Be(ExternalApiStatus.TransientFailure);
        result.Error.Should().Contain("connection refused");
    }

    // ---- validacao de entrada / SSRF ----

    [Fact]
    public async Task Missing_access_token_fails_permanently_without_any_call()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));

        var result = await Blocker(handler).BlockAsync(Request(token: null));

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("loja.myshopify.com.evil.com")]
    [InlineData("http://loja.myshopify.com")]
    [InlineData("loja.myshopify.com/admin")]
    [InlineData("loja@evil.com")]
    [InlineData("127.0.0.1")]
    [InlineData("")]
    public async Task Only_real_myshopify_domains_are_ever_called(string shop)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));

        var result = await Blocker(handler).BlockAsync(Request(shop: shop));

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1/../../x")]
    [InlineData("12 34")]
    [InlineData("")]
    public async Task Non_numeric_order_ids_never_reach_the_url(string orderId)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));

        var result = await Blocker(handler).BlockAsync(Request(orderId: orderId));

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Base_url_and_api_version_are_configurable()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "on_hold", null))));

        await Blocker(handler, baseUrl: "https://shopify.sim.test/", version: "2099-01").BlockAsync(Request());

        handler.Requests[0].Uri.AbsoluteUri.Should().Be("https://shopify.sim.test/admin/api/2099-01/orders/820982911946154508/fulfillment_orders.json");
    }

    // ---- liberacao ----

    [Fact]
    public async Task Release_posts_release_hold_for_holds_placed_by_riskguard()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((111, "on_hold", ["RiskGuard: score 90"]), (222, "open", null)))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await Blocker(handler).ReleaseAsync(Request());

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].Uri.AbsolutePath.Should().EndWith("/fulfillment_orders/111/release_hold.json");
        handler.Requests[1].Body.Should().Be("{}");
    }

    [Fact]
    public async Task Release_refuses_to_touch_holds_that_were_not_placed_by_riskguard()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "on_hold", ["RiskGuard: x", "Aguardando o cliente"]))));

        var result = await Blocker(handler).ReleaseAsync(Request());

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        result.Error.Should().Contain("outra origem");
        handler.Requests.Should().ContainSingle(); // nenhum release_hold foi enviado
    }

    [Fact]
    public async Task Release_with_nothing_on_hold_is_an_idempotent_success()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "open", null))));

        var result = await Blocker(handler).ReleaseAsync(Request());

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Release_failures_are_classified()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, FulfillmentOrders((1, "on_hold", ["RiskGuard: x"])))
            : FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable));

        (await Blocker(handler).ReleaseAsync(Request())).Status.Should().Be(ExternalApiStatus.TransientFailure);
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var blocker = Blocker(new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK)));

        await ((Func<Task>)(() => blocker.BlockAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => blocker.ReleaseAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }
}
