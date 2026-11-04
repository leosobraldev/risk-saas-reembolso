using RiskGuard.Core.Abstractions;
using RiskGuard.Modules.Action.Application.Blockers;
using RiskGuard.Modules.Tenant.Domain;
using static RiskGuard.UnitTests.Platforms.HttpTestKit;

namespace RiskGuard.UnitTests.Tenant;

public class TenantSettingsUpdateTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(50, false)]
    [InlineData(100, true)]
    public void Valid_values_are_applied(int threshold, bool autoBlock)
    {
        var settings = TenantSettings.CreateDefault(Guid.NewGuid());

        var result = settings.Update(threshold, autoBlock);

        result.IsSuccess.Should().BeTrue();
        settings.HighRiskThreshold.Should().Be(threshold);
        settings.AutoBlockEnabled.Should().Be(autoBlock);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Out_of_range_thresholds_are_rejected_and_nothing_changes(int threshold)
    {
        var settings = TenantSettings.CreateDefault(Guid.NewGuid());

        var result = settings.Update(threshold, autoBlockEnabled: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Validation);
        settings.HighRiskThreshold.Should().Be(80);
        settings.AutoBlockEnabled.Should().BeFalse();
    }
}

public class PlatformConnectionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_the_platform_and_keeps_only_protected_values()
    {
        var tenantId = Guid.NewGuid();

        var connection = PlatformConnection.Create(tenantId, " Shopify ", " loja.myshopify.com ", "v1:cipher-secret", "v1:cipher-token", Now);

        connection.TenantId.Should().Be(tenantId);
        connection.Platform.Should().Be("shopify");
        connection.AccountId.Should().Be("loja.myshopify.com");
        connection.WebhookSecretProtected.Should().Be("v1:cipher-secret");
        connection.AccessTokenProtected.Should().Be("v1:cipher-token");
        connection.CreatedAt.Should().Be(Now);
        connection.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Replace_updates_the_values_and_the_timestamp_but_not_the_creation_time()
    {
        var connection = PlatformConnection.Create(Guid.NewGuid(), "shopify", "a.myshopify.com", "s1", "t1", Now);

        connection.Replace("b.myshopify.com", "s2", null, Now.AddHours(1));

        connection.AccountId.Should().Be("b.myshopify.com");
        connection.WebhookSecretProtected.Should().Be("s2");
        connection.AccessTokenProtected.Should().BeNull();
        connection.CreatedAt.Should().Be(Now);
        connection.UpdatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Creation_rejects_invalid_input()
    {
        ((Action)(() => PlatformConnection.Create(Guid.Empty, "shopify", "a", "s", null, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => PlatformConnection.Create(Guid.NewGuid(), " ", "a", "s", null, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => PlatformConnection.Create(Guid.NewGuid(), "shopify", "", "s", null, Now))).Should().Throw<ArgumentException>();
        ((Action)(() => PlatformConnection.Create(Guid.NewGuid(), "shopify", "a", "", null, Now))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encryption_contexts_bind_each_secret_to_tenant_platform_and_purpose()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        PlatformConnection.WebhookSecretContext(a, "Shopify").Should().Be(PlatformConnection.WebhookSecretContext(a, "shopify"));
        PlatformConnection.WebhookSecretContext(a, "shopify").Should().NotBe(PlatformConnection.WebhookSecretContext(b, "shopify"));
        PlatformConnection.WebhookSecretContext(a, "shopify").Should().NotBe(PlatformConnection.WebhookSecretContext(a, "nuvemshop"));
        PlatformConnection.WebhookSecretContext(a, "shopify").Should().NotBe(PlatformConnection.AccessTokenContext(a, "shopify"));
    }
}

public class OrderBlockerResolverTests
{
    private static IHttpClientFactory Http => Factory(new Platforms.FakeHttpHandler(_ => new HttpResponseMessage()));

    private static OrderBlockerResolver Create() =>
        new([new ShopifyOrderBlocker(Http, Options()), new NuvemshopOrderBlocker(Http, Options())]);

    [Theory]
    [InlineData("shopify")]
    [InlineData("SHOPIFY")]
    [InlineData("Nuvemshop")]
    public void Finds_blockers_case_insensitively(string platform)
    {
        Create().Resolve(platform)!.Platform.Should().Be(platform.ToLowerInvariant());
    }

    [Theory]
    [InlineData("magento")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_platforms_resolve_to_null(string? platform)
    {
        Create().Resolve(platform).Should().BeNull();
    }

    [Fact]
    public void Duplicates_are_rejected()
    {
        var act = () => new OrderBlockerResolver([new ShopifyOrderBlocker(Http, Options()), new ShopifyOrderBlocker(Http, Options())]);

        act.Should().Throw<InvalidOperationException>();
    }
}
