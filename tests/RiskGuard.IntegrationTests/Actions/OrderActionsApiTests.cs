using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Contracts;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Action.Jobs;
using static RiskGuard.IntegrationTests.Actions.ActionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Actions;

/// <summary>O lojista ve o que o RiskGuard fez (GET /actions) e pode liberar um pedido bloqueado (POST /actions/{id}/release).</summary>
[Collection(InfrastructureCollection.Name)]
public class OrderActionsApiTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri List = new("/api/v1/actions", UriKind.Relative);

    private static Uri Release(Guid id) => new($"/api/v1/actions/{id}/release", UriKind.Relative);

    private ApiFactory Api => infrastructure.Api;

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        using var anonymous = Api.CreateClient();

        (await anonymous.GetAsync(List)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(Release(Guid.NewGuid()), content: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lists_only_the_callers_actions_newest_first_and_supports_filter_and_paging()
    {
        var (tenantA, emailA) = await RegisterAsync(Api);
        var (tenantB, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantA, autoBlock: true, threshold: 40);
        using var anonymous = Api.CreateClient();
        var first = await BlockAsync(anonymous, tenantA);
        var second = await BlockAsync(anonymous, tenantA);
        var other = await BlockAsync(anonymous, tenantB, configure: true);
        using var client = await AuthenticatedClientAsync(Api, emailA);

        var page = await client.GetFromJsonAsync<PageBody>(List);
        var paged = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/actions?page=2&pageSize=1", UriKind.Relative));
        var succeeded = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/actions?status=Succeeded", UriKind.Relative));
        var failed = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/actions?status=Failed", UriKind.Relative));

        page!.Total.Should().Be(2);
        page.Items.Select(i => i.ExternalOrderId).Should().Equal(second, first); // mais recente primeiro
        page.Items.Should().NotContain(i => i.ExternalOrderId == other);
        page.Items[0].Should().Match<ActionBody>(a => a.Type == "Block" && a.Status == "Succeeded" && a.Platform == "shopify" && a.Score == 50);
        paged!.Items.Should().ContainSingle().Which.ExternalOrderId.Should().Be(first);
        succeeded!.Total.Should().Be(2);
        failed!.Total.Should().Be(0);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("status=Nope")]
    public async Task Invalid_query_parameters_return_400(string query)
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.GetAsync(new Uri($"/api/v1/actions?{query}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Page_size_is_capped_at_one_hundred()
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/actions?pageSize=100000", UriKind.Relative)))!.PageSize.Should().Be(100);
    }

    // ---- liberacao ----

    [Fact]
    public async Task Releasing_a_blocked_shopify_order_lifts_the_hold_asynchronously()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        using var anonymous = Api.CreateClient();
        var orderId = await BlockAsync(anonymous, tenantId);
        var action = (await FindByExternalIdAsync(infrastructure, tenantId, orderId))!;
        Fake(Api).ShopifyStatuses(orderId).Should().Equal("on_hold");
        using var client = await AuthenticatedClientAsync(Api, email);

        var response = await client.PostAsync(Release(action.Id), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<ActionBody>())!.Status.Should().Be("Releasing");
        var released = await WaitForStatusAsync(infrastructure, tenantId, orderId, "Released");
        released.LastError.Should().BeNull();
        Fake(Api).ShopifyStatuses(orderId).Should().Equal("open");
        Fake(Api).ShopifyHoldNotes(orderId).Should().BeEmpty();
        (await client.PostAsync(Release(action.Id), content: null)).StatusCode.Should().Be(HttpStatusCode.Conflict); // ja liberado
    }

    [Fact]
    public async Task Releasing_a_flagged_nuvemshop_order_removes_only_the_riskguard_note()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        var orderId = NewExternalId();
        Fake(Api).AddNuvemshopOrder(orderId, "fraude@teste.com", "10.00", note: "Embrulhar para presente");
        using var anonymous = Api.CreateClient();
        var body = $$"""{ "id": {{orderId}}, "contact_email": "fraude@teste.com", "total": "10.00", "created_at": "2026-03-01T09:11:51+0000" }""";
        (await WebhookTestClient.PostAsync(anonymous, "nuvemshop", tenantId, body)).EnsureSuccessStatusCode();
        var action = await WaitForStatusAsync(infrastructure, tenantId, orderId, "Succeeded");
        Fake(Api).NuvemshopNote(orderId).Should().Contain("[RiskGuard]");
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PostAsync(Release(action.Id), content: null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await WaitForStatusAsync(infrastructure, tenantId, orderId, "Released");

        Fake(Api).NuvemshopNote(orderId).Should().Be("Embrulhar para presente");
    }

    [Fact]
    public async Task A_hold_placed_by_the_merchant_is_never_released_by_riskguard()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        using var anonymous = Api.CreateClient();
        var orderId = await BlockAsync(anonymous, tenantId);
        Fake(Api).AddMerchantHold(orderId, "Hold manual do lojista"); // alguem mais colocou um hold na mesma fulfillment order
        var action = (await FindByExternalIdAsync(infrastructure, tenantId, orderId))!;
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PostAsync(Release(action.Id), content: null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var failed = await WaitForStatusAsync(infrastructure, tenantId, orderId, "ReleaseFailed");

        failed.LastError.Should().Contain("outra origem");
        Fake(Api).ShopifyStatuses(orderId).Should().Equal("on_hold"); // nada foi liberado
        Fake(Api).CallsFor(orderId).Should().NotContain(c => c.Url.AbsolutePath.EndsWith("release_hold.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_applied_actions_can_be_released_and_other_tenants_actions_look_nonexistent()
    {
        var (tenantA, emailA) = await RegisterAsync(Api); // auto-block desligado => Skipped
        var (_, emailB) = await RegisterAsync(Api);
        await SeedRefundsAsync(infrastructure, tenantA, "fraude@teste.com", 4);
        using var anonymous = Api.CreateClient();
        var orderId = await PostShopifyOrderAsync(anonymous, tenantA, "fraude@teste.com");
        var skipped = await WaitForStatusAsync(infrastructure, tenantA, orderId, "Skipped");
        using var a = await AuthenticatedClientAsync(Api, emailA);
        using var b = await AuthenticatedClientAsync(Api, emailB);

        var notReleasable = await a.PostAsync(Release(skipped.Id), content: null);
        (await notReleasable.Content.ReadAsStringAsync()).Should().Contain("action.not_releasable");

        notReleasable.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await a.PostAsync(Release(Guid.NewGuid()), content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.PostAsync(Release(skipped.Id), content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound); // outro tenant: indistinguivel de inexistente
    }

    [Fact]
    public async Task Asking_twice_while_releasing_enqueues_a_single_job_and_a_failed_release_can_be_retried()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false, hangfireMaxRetries: 0);
        var (tenantId, email) = await RegisterAsync(factory);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true);
        var orderId = NewExternalId();
        await PublishHighRiskAsync(factory, tenantId, orderId);
        var action = (await FindByExternalIdAsync(infrastructure, tenantId, orderId))!;
        await RunJobAsync(factory, tenantId, action.Id); // aplica o bloqueio (servidor desligado: executamos o job na mao)
        using var client = await AuthenticatedClientAsync(factory, email);
        var before = CountJobsFor(factory, action.Id);

        var first = await client.PostAsync(Release(action.Id), content: null);
        var second = await client.PostAsync(Release(action.Id), content: null);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await second.Content.ReadFromJsonAsync<ActionBody>())!.Status.Should().Be("Releasing");
        CountJobsFor(factory, action.Id).Should().Be(before + 1); // so um job de liberacao

        // a plataforma falha de forma transitoria e sem retries restantes: ReleaseFailed; o lojista pode pedir de novo
        Fake(factory).Script(orderId, HttpStatusCode.ServiceUnavailable);
        await ((Func<Task>)(() => RunJobAsync(factory, tenantId, action.Id))).Should().ThrowAsync<RiskGuard.Infrastructure.ExternalApi.TransientExternalApiException>();
        (await FindByExternalIdAsync(infrastructure, tenantId, orderId))!.Status.Should().Be("ReleaseFailed");

        (await client.PostAsync(Release(action.Id), content: null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RunJobAsync(factory, tenantId, action.Id);
        (await FindByExternalIdAsync(infrastructure, tenantId, orderId))!.Status.Should().Be("Released");
    }

    // ---- helpers ----

    /// <summary>Posta um pedido de risco 50 (e-mail suspeito) e espera o bloqueio. Com <paramref name="configure"/>, liga o auto-block antes.</summary>
    private async Task<string> BlockAsync(HttpClient anonymous, Guid tenantId, bool configure = false)
    {
        if (configure)
        {
            await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        }

        var orderId = await PostShopifyOrderAsync(anonymous, tenantId, "fraude@teste.com");
        await WaitForStatusAsync(infrastructure, tenantId, orderId, "Succeeded");
        return orderId;
    }

    private static async Task PublishHighRiskAsync(ApiFactory factory, Guid tenantId, string orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(
            new RiskCalculatedEvent(tenantId, Guid.NewGuid(), Guid.NewGuid(), "shopify", orderId, Score: 90, RiskLevel.High));
    }

    private static async Task RunJobAsync(ApiFactory factory, Guid tenantId, Guid actionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ExecuteOrderActionJob>().RunAsync(tenantId, actionId);
    }

    private static int CountJobsFor(ApiFactory factory, Guid actionId)
    {
        var api = factory.Services.GetRequiredService<Hangfire.JobStorage>().GetMonitoringApi();
        return api.EnqueuedJobs("default", 0, 100).Count(j => j.Value.Job?.Args.Contains(actionId) == true);
    }

    private sealed record PageBody(List<ActionBody> Items, int Page, int PageSize, int Total);

    private sealed record ActionBody(Guid Id, Guid EvaluationId, Guid OrderId, string Platform, string ExternalOrderId, int Score, string Type, string Status, int Attempts, string? LastError);
}
