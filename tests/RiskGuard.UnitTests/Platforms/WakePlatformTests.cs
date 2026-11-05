using System.Net;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Modules.Ingestion.Application;
using static RiskGuard.UnitTests.Platforms.HttpTestKit;

namespace RiskGuard.UnitTests.Platforms;

public class WakeOrderFetcherTests
{
    private static RiskGuard.Core.Contracts.PlatformCredentials Creds(string? token = "tok", string store = "NomeLoja") => Credentials("wake", store, token);

    [Fact]
    public async Task Fetches_the_order_from_the_fbits_api_with_a_bearer_token()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, """{"pedidoId":123}"""));
        var fetcher = new WakeOrderFetcher(Factory(handler), Options());

        var result = await fetcher.FetchOrderAsync(Creds(), "123");

        result.IsSuccess.Should().BeTrue();
        result.Body.Should().Be("""{"pedidoId":123}""");
        handler.Requests.Single().Method.Should().Be(HttpMethod.Get);
        handler.Requests.Single().Uri.AbsoluteUri.Should().Be("https://api.fbits.net/pedidos/123");
        handler.Requests.Single().Headers["Authorization"].Should().Be("Bearer tok");
    }

    [Fact]
    public async Task The_api_root_can_be_overridden_by_configuration()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, "{}"));
        var fetcher = new WakeOrderFetcher(Factory(handler), Options("wake", "https://wake.sim.test/"));

        await fetcher.FetchOrderAsync(Creds(), "7");

        handler.Requests.Single().Uri.AbsoluteUri.Should().Be("https://wake.sim.test/pedidos/7");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ExternalApiStatus.PermanentFailure)]
    [InlineData(HttpStatusCode.Unauthorized, ExternalApiStatus.PermanentFailure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ExternalApiStatus.TransientFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, ExternalApiStatus.TransientFailure)]
    public async Task Failures_are_classified(HttpStatusCode status, ExternalApiStatus expected)
    {
        var fetcher = new WakeOrderFetcher(Factory(new FakeHttpHandler(_ => FakeHttpHandler.Status(status))), Options());

        (await fetcher.FetchOrderAsync(Creds(), "123")).Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "123")]
    [InlineData("", "123")]
    [InlineData("tok", "123/status")]
    [InlineData("tok", "../pedidos/1")]
    [InlineData("tok", "abc")]
    [InlineData("tok", "")]
    public async Task Invalid_input_fails_permanently_without_calling_out(string? token, string orderId)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));
        var fetcher = new WakeOrderFetcher(Factory(handler), Options());

        var result = await fetcher.FetchOrderAsync(Creds(token), orderId);

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Platform_is_wake()
    {
        new WakeOrderFetcher(Substitute.For<IHttpClientFactory>(), Options()).Platform.Should().Be("wake");
    }
}

public class PlatformCatalogTests
{
    [Fact]
    public void Wake_is_a_supported_platform()
    {
        PlatformIds.IsSupported("wake").Should().BeTrue();
        PlatformIds.IsSupported(" WAKE ").Should().BeTrue();
        PlatformIds.All.Should().Contain(["shopify", "nuvemshop", "wake"]);
    }

    [Theory]
    [InlineData("NomeLoja", true)]
    [InlineData("minha-loja", true)]
    [InlineData("loja_01.teste", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("-comeca-com-hifen", false)]
    [InlineData("com espaco", false)]
    [InlineData("loja/../x", false)]
    [InlineData("http://evil.test", false)]
    public void Wake_store_names_are_validated(string accountId, bool valid)
    {
        PlatformIds.IsValidAccountId("wake", accountId).Should().Be(valid);
    }

    [Fact]
    public void The_catalog_describes_every_supported_platform_exactly_once()
    {
        PlatformCatalog.All.Select(p => p.Id).Should().BeEquivalentTo(PlatformIds.All);
        PlatformCatalog.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        PlatformCatalog.All.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.AccountIdLabel));
    }

    [Fact]
    public void Wake_needs_the_access_token_to_fetch_orders_and_has_no_automatic_action_yet()
    {
        var wake = PlatformCatalog.Find("wake")!;

        wake.Name.Should().Be("Wake");
        wake.RequiresAccessToken.Should().BeTrue();   // o webhook nao traz e-mail nem valor: o pedido e buscado na API
        wake.SupportsActions.Should().BeFalse();      // a Wake nao tem "reter pedido": so avaliacao por enquanto
    }

    [Fact]
    public void Shopify_and_Nuvemshop_keep_their_behavior()
    {
        PlatformCatalog.Find("shopify")!.SupportsActions.Should().BeTrue();
        PlatformCatalog.Find("nuvemshop")!.SupportsActions.Should().BeTrue();
        PlatformCatalog.Find("shopify")!.RequiresAccessToken.Should().BeFalse();
        PlatformCatalog.Find("nuvemshop")!.RequiresAccessToken.Should().BeFalse();
    }

    [Fact]
    public void Lookup_is_case_insensitive_and_unknown_is_null()
    {
        PlatformCatalog.Find("WAKE")!.Id.Should().Be("wake");
        PlatformCatalog.Find("magento").Should().BeNull();
        PlatformCatalog.Find(null).Should().BeNull();
    }

    [Fact]
    public void Platforms_with_an_order_blocker_are_exactly_those_that_support_actions()
    {
        // o catalogo vive no Core (sem referenciar o modulo Action): este teste impede que os dois divirjam
        var blockers = new RiskGuard.Modules.Action.Application.Blockers.IOrderBlocker[]
        {
            new RiskGuard.Modules.Action.Application.Blockers.ShopifyOrderBlocker(Substitute.For<IHttpClientFactory>(), Options()),
            new RiskGuard.Modules.Action.Application.Blockers.NuvemshopOrderBlocker(Substitute.For<IHttpClientFactory>(), Options()),
        };

        PlatformCatalog.All.Where(p => p.SupportsActions).Select(p => p.Id)
            .Should().BeEquivalentTo(blockers.Select(b => b.Platform));
    }
}
