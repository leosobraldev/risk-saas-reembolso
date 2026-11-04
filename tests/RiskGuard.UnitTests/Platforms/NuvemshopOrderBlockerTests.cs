using System.Net;
using System.Text.Json;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Modules.Action.Application.Blockers;
using RiskGuard.Modules.Action.Domain;
using static RiskGuard.UnitTests.Platforms.HttpTestKit;

namespace RiskGuard.UnitTests.Platforms;

/// <summary>Nuvemshop nao tem hold: o bloqueio e uma SINALIZACAO nao destrutiva (nota interna), nunca cancelamento.</summary>
public class NuvemshopOrderBlockerTests
{
    private static BlockRequest Request(string orderId = "4501", string? token = "tok", string store = "123456") =>
        new(Guid.NewGuid(), orderId, 90, "score", Credentials("nuvemshop", store, token));

    private static string Order(string status, string? note) =>
        JsonSerializer.Serialize(new { id = 4501, status, owner_note = note });

    private static NuvemshopOrderBlocker Blocker(FakeHttpHandler handler, string? baseUrl = null) =>
        new(Factory(handler), Options(baseUrl is null ? null : "nuvemshop", baseUrl));

    [Fact]
    public void Nuvemshop_can_only_flag_never_pause()
    {
        var blocker = Blocker(new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK)));

        blocker.Platform.Should().Be("nuvemshop");
        blocker.ActionType.Should().Be(OrderActionType.Flag);
    }

    [Fact]
    public async Task Flag_reads_the_order_then_appends_a_marker_preserving_the_merchants_note()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", "Cliente pediu embalagem de presente"))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await Blocker(handler).BlockAsync(Request());

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Uri.AbsoluteUri.Should().Be("https://api.nuvemshop.com.br/v1/123456/orders/4501?fields=id,status,owner_note");
        handler.Requests[1].Method.Should().Be(HttpMethod.Put);
        handler.Requests[1].Uri.AbsoluteUri.Should().Be("https://api.nuvemshop.com.br/v1/123456/orders/4501");
        handler.Requests.Should().OnlyContain(r => r.Headers["Authentication"] == "bearer tok");

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        var note = body.RootElement.GetProperty("owner_note").GetString()!;
        note.Should().StartWith("Cliente pediu embalagem de presente\n");
        note.Should().Contain($"{NuvemshopOrderBlocker.Marker} Risco alto (score 90)");
    }

    [Fact]
    public async Task Flag_on_an_order_without_a_note_writes_only_the_marker_line()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", null))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        await Blocker(handler).BlockAsync(Request());

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("owner_note").GetString().Should().StartWith(NuvemshopOrderBlocker.Marker);
    }

    [Fact]
    public async Task Already_flagged_orders_are_left_alone()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", $"{NuvemshopOrderBlocker.Marker} antigo")));

        var result = await Blocker(handler).BlockAsync(Request());

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().ContainSingle(); // sem PUT: idempotente
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cancelled")]
    public async Task Finished_orders_fail_permanently(string status)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, Order(status, null)));

        var result = await Blocker(handler).BlockAsync(Request());

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ExternalApiStatus.PermanentFailure)]
    [InlineData(HttpStatusCode.Unauthorized, ExternalApiStatus.PermanentFailure)]
    [InlineData(HttpStatusCode.InternalServerError, ExternalApiStatus.TransientFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, ExternalApiStatus.TransientFailure)]
    public async Task Read_failures_are_classified(HttpStatusCode status, ExternalApiStatus expected)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(status));

        (await Blocker(handler).BlockAsync(Request())).Status.Should().Be(expected);
    }

    [Fact]
    public async Task Write_failures_are_classified()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", null))
            : FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable));

        (await Blocker(handler).BlockAsync(Request())).Status.Should().Be(ExternalApiStatus.TransientFailure);
    }

    [Fact]
    public async Task Malformed_json_fails_permanently()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, "<html>"));

        (await Blocker(handler).BlockAsync(Request())).Status.Should().Be(ExternalApiStatus.PermanentFailure);
    }

    [Fact]
    public async Task Missing_token_fails_permanently_without_any_call()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));

        var result = await Blocker(handler).BlockAsync(Request(token: " "));

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("4501", "abc")]
    [InlineData("4501", "12/../x")]
    [InlineData("4501/close", "123")]
    [InlineData("abc", "123")]
    public async Task Only_numeric_store_and_order_ids_reach_the_url(string orderId, string store)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));

        var result = await Blocker(handler).BlockAsync(Request(orderId, store: store));

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Base_url_is_configurable()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", $"{NuvemshopOrderBlocker.Marker} x")));

        await Blocker(handler, "https://nuvemshop.sim.test/").BlockAsync(Request());

        handler.Requests[0].Uri.Host.Should().Be("nuvemshop.sim.test");
    }

    // ---- liberacao ----

    [Fact]
    public async Task Release_removes_only_the_riskguard_line()
    {
        var note = $"Pedido de presente\n{NuvemshopOrderBlocker.Marker} Risco alto (score 90): revise\nLigar antes de enviar";
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", note))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await Blocker(handler).ReleaseAsync(Request());

        result.IsSuccess.Should().BeTrue();
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("owner_note").GetString().Should().Be("Pedido de presente\nLigar antes de enviar");
    }

    [Fact]
    public async Task Release_can_leave_an_empty_note()
    {
        var handler = new FakeHttpHandler(r => r.Method == HttpMethod.Get
            ? FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", $"{NuvemshopOrderBlocker.Marker} so a marca"))
            : FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));

        await Blocker(handler).ReleaseAsync(Request());

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("owner_note").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task Release_without_the_marker_is_an_idempotent_success()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, Order("open", "nota do lojista")));

        var result = await Blocker(handler).ReleaseAsync(Request());

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var blocker = Blocker(new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK)));

        await ((Func<Task>)(() => blocker.BlockAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => blocker.ReleaseAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }
}
