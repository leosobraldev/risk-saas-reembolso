using RiskGuard.Modules.Ingestion.Application;
using RiskGuard.Modules.Ingestion.Application.Parsing;

namespace RiskGuard.UnitTests.Ingestion;

public class WebhookParserResolverAndHeadersTests
{
    private readonly WebhookParserResolver _resolver = new([new ShopifyWebhookParser(), new NuvemshopWebhookParser()]);

    [Theory]
    [InlineData("shopify", "shopify")]
    [InlineData("SHOPIFY", "shopify")]
    [InlineData("Nuvemshop", "nuvemshop")]
    public void Resolves_the_strategy_by_platform_case_insensitively(string route, string expected)
    {
        _resolver.Resolve(route)!.Platform.Should().Be(expected);
    }

    [Theory]
    [InlineData("magento")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_platforms_resolve_to_null(string? route)
    {
        _resolver.Resolve(route).Should().BeNull();
    }

    [Fact]
    public void Duplicate_platform_registrations_fail_fast()
    {
        var act = () => new WebhookParserResolver([new ShopifyWebhookParser(), new ShopifyWebhookParser()]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sensitive_headers_are_never_persisted()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer secret",
            ["COOKIE"] = "session=1",
            ["Proxy-Authorization"] = "x",
            ["X-Api-Key"] = "k",
            ["X-Shopify-Topic"] = "orders/create",
            ["X-Shopify-Hmac-Sha256"] = "abc",
            ["Content-Type"] = "application/json",
        };

        var json = WebhookHeaders.ToJson(headers);

        json.Should().Contain("X-Shopify-Topic").And.Contain("X-Shopify-Hmac-Sha256").And.Contain("Content-Type");
        json.Should().NotContainEquivalentOf("secret").And.NotContainEquivalentOf("session=1")
            .And.NotContainEquivalentOf("Authorization").And.NotContainEquivalentOf("Cookie")
            .And.NotContainEquivalentOf("X-Api-Key");
    }

    [Fact]
    public void Headers_are_bounded_in_count_and_value_size()
    {
        var many = Enumerable.Range(0, 500).ToDictionary(i => $"X-H-{i}", _ => "v");
        many["X-Long"] = new string('a', 10_000);

        var json = WebhookHeaders.ToJson(many);

        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!
            .Should().HaveCountLessThanOrEqualTo(WebhookHeaders.MaxHeaders)
            .And.OnlyContain(kv => kv.Value.Length <= WebhookHeaders.MaxValueLength);
    }
}
