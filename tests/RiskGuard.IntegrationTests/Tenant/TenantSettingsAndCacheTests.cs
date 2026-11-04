using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Tenant.Application.Queries;
using RiskGuard.Modules.Tenant.Application.Settings;
using static RiskGuard.IntegrationTests.Actions.ActionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Tenant;

/// <summary>Mudancas de configuracao valem na hora: o snapshot do tenant no Redis e invalidado (nao espera os 5 min do TTL).</summary>
[Collection(InfrastructureCollection.Name)]
public class TenantSettingsAndCacheTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri Settings = new("/api/v1/tenants/me/settings", UriKind.Relative);

    private ApiFactory Api => infrastructure.Api;

    private static string CacheKey(Guid tenantId) => $"tenant:{tenantId}:snapshot";

    [Fact]
    public async Task Updating_settings_requires_authentication()
    {
        using var anonymous = Api.CreateClient();

        (await anonymous.PutAsJsonAsync(Settings, new { highRiskThreshold = 50, autoBlockEnabled = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(101, true)]
    [InlineData(null, true)]
    [InlineData(50, null)]
    public async Task Invalid_settings_are_rejected_with_400_and_nothing_changes(int? threshold, bool? autoBlock)
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        var response = await client.PutAsJsonAsync(Settings, new { highRiskThreshold = threshold, autoBlockEnabled = autoBlock });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var me = await client.GetFromJsonAsync<TenantBody>(new Uri("/api/v1/tenants/me", UriKind.Relative));
        me!.Settings.Should().Be(new SettingsBody(80, false));
    }

    [Fact]
    public async Task Valid_settings_are_saved_and_visible_in_me()
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        var response = await client.PutAsJsonAsync(Settings, new { highRiskThreshold = 65, autoBlockEnabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SettingsBody>()).Should().Be(new SettingsBody(65, true));
        (await client.GetFromJsonAsync<TenantBody>(new Uri("/api/v1/tenants/me", UriKind.Relative)))!.Settings.Should().Be(new SettingsBody(65, true));
    }

    [Fact]
    public async Task One_tenants_settings_never_change_another()
    {
        var (_, emailA) = await RegisterAsync(Api);
        var (_, emailB) = await RegisterAsync(Api);
        using var a = await AuthenticatedClientAsync(Api, emailA);
        using var b = await AuthenticatedClientAsync(Api, emailB);

        await a.PutAsJsonAsync(Settings, new { highRiskThreshold = 10, autoBlockEnabled = true });

        (await b.GetFromJsonAsync<TenantBody>(new Uri("/api/v1/tenants/me", UriKind.Relative)))!.Settings.Should().Be(new SettingsBody(80, false));
    }

    [Fact]
    public async Task The_cached_snapshot_is_invalidated_when_the_settings_change()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);
        await using var scope = Api.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        (await sender.Send(new GetTenantSnapshotQuery(tenantId)))!.HighRiskThreshold.Should().Be(80); // popula o cache
        (await cache.GetAsync<TenantSnapshotCacheEntry>(CacheKey(tenantId))).Should().NotBeNull();

        (await client.PutAsJsonAsync(Settings, new { highRiskThreshold = 55, autoBlockEnabled = true })).EnsureSuccessStatusCode();

        (await cache.GetAsync<TenantSnapshotCacheEntry>(CacheKey(tenantId))).Should().BeNull(); // invalidado
        var fresh = (await sender.Send(new GetTenantSnapshotQuery(tenantId)))!;
        fresh.HighRiskThreshold.Should().Be(55);
        fresh.AutoBlockEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Turning_autoblock_on_takes_effect_for_the_very_next_order_even_with_a_warm_cache()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        await SeedRefundsAsync(infrastructure, tenantId, "fraude@teste.com", 4);
        using var anonymous = Api.CreateClient();
        using var client = await AuthenticatedClientAsync(Api, email);

        var before = await PostShopifyOrderAsync(anonymous, tenantId, "fraude@teste.com"); // aquece o cache com auto-block DESLIGADO
        await WaitForStatusAsync(infrastructure, tenantId, before, "Skipped");

        (await client.PutAsJsonAsync(Settings, new { highRiskThreshold = 80, autoBlockEnabled = true })).EnsureSuccessStatusCode();
        var after = await PostShopifyOrderAsync(anonymous, tenantId, "fraude@teste.com");
        await WaitForStatusAsync(infrastructure, tenantId, after, "Succeeded"); // sem esperar o TTL de 5 min

        Fake(Api).ShopifyStatuses(before).Should().Equal("open");
        Fake(Api).ShopifyStatuses(after).Should().Equal("on_hold");
    }

    [Fact]
    public async Task Deactivating_a_tenant_stops_its_webhooks_immediately_despite_the_cache()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        using var client = Api.CreateClient();
        var body = $$"""{ "id": {{NewExternalId()}}, "email": "maria@gmail.com", "created_at": "2026-03-01T10:15:30Z", "total_price": "10.00" }""";
        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, body)).StatusCode.Should().Be(HttpStatusCode.Accepted); // cache aquecido

        await using (var scope = Api.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DeactivateTenantCommand(tenantId))).IsSuccess.Should().BeTrue();
        }

        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, body.Replace("10.00", "11.00"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivating_an_unknown_tenant_reports_not_found()
    {
        await using var scope = Api.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DeactivateTenantCommand(Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    private sealed record SettingsBody(int HighRiskThreshold, bool AutoBlockEnabled);

    private sealed record TenantBody(Guid Id, string Name, string Email, SettingsBody Settings);
}
