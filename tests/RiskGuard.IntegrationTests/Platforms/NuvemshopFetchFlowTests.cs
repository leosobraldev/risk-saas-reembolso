using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Actions.ActionTestHarness;
using static RiskGuard.IntegrationTests.Ingestion.IngestionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Platforms;

/// <summary>Webhooks reais da Nuvemshop trazem so { store_id, event, id }: o pedido e buscado na API da loja com o token do lojista.</summary>
[Collection(InfrastructureCollection.Name)]
public class NuvemshopFetchFlowTests(InfrastructureFixture infrastructure)
{
    private static string IdOnly(string orderId, string storeId) => $$"""{ "store_id": {{storeId}}, "event": "order/created", "id": {{orderId}} }""";

    private async Task<Guid> PostIdOnlyAsync(HttpClient client, Guid tenantId, string orderId)
    {
        var storeId = TestCredentialStore.Get(tenantId, "nuvemshop")!.AccountId;
        var response = await WebhookTestClient.PostAsync(client, "nuvemshop", tenantId, IdOnly(orderId, storeId));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId;
    }

    [Fact]
    public async Task An_id_only_webhook_is_completed_by_fetching_the_order_and_flows_through_risk_and_action()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        var orderId = NewExternalId();
        infrastructure.Api.Services.GetRequiredService<FakePlatformsHandler>().AddNuvemshopOrder(orderId, "Fraude@Teste.com", "250.00");
        using var client = infrastructure.Api.CreateClient();

        var webhookId = await PostIdOnlyAsync(client, tenantId, orderId);
        await WaitForAsync(infrastructure, webhookId, s => s.Status == "Processed");

        var risk = await WaitForRiskEventAsync(infrastructure.Api, tenantId, orderId);
        risk.Score.Should().Be(50); // e-mail suspeito vindo do pedido BUSCADO (o webhook nao tinha e-mail)
        var ingested = infrastructure.Api.Services.GetRequiredService<OrderIngestedCapture>().Events.Single(e => e.ExternalOrderId == orderId && e.TenantId == tenantId);
        ingested.CustomerEmail.Should().Be("fraude@teste.com");
        ingested.TotalAmount.Should().Be(250.00m);
        ingested.Platform.Should().Be("nuvemshop");

        // a busca usou a API real da Nuvemshop com o token do lojista, e o cadeia seguiu ate a sinalizacao
        var fake = infrastructure.Api.Services.GetRequiredService<FakePlatformsHandler>();
        fake.CallsFor(orderId)[0].Method.Should().Be("GET");
        await WaitForStatusAsync(infrastructure, tenantId, orderId, "Succeeded");
        fake.NuvemshopNote(orderId).Should().Contain("[RiskGuard]");
    }

    [Fact]
    public async Task An_order_missing_on_the_platform_is_audited_and_marked_failed_without_retry()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var webhookId = await PostIdOnlyAsync(client, tenantId, NewExternalId()); // a Nuvemshop falsa nao conhece este pedido (404)
        var state = await WaitForAsync(infrastructure, webhookId, s => s.Status == "Failed");

        state.LastError.Should().Contain("404");
        state.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task A_transient_platform_error_is_recorded_as_an_attempt_and_the_webhook_stays_pending_for_retry()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        var orderId = NewExternalId();
        var fake = infrastructure.Api.Services.GetRequiredService<FakePlatformsHandler>();
        fake.AddNuvemshopOrder(orderId, "maria@gmail.com", "10.00");
        fake.Script(orderId, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);
        using var client = infrastructure.Api.CreateClient();

        var webhookId = await PostIdOnlyAsync(client, tenantId, orderId);
        var state = await WaitForAsync(infrastructure, webhookId, s => s.Attempts >= 1);

        state.Status.Should().Be("Received"); // nao e Failed: o Hangfire tentara de novo (RNF04)
        state.LastError.Should().Contain("503");
    }

    [Fact]
    public async Task Without_an_access_token_the_order_cannot_be_fetched_and_fails_permanently()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api);
        using var authed = await AuthenticatedClientAsync(infrastructure.Api, email);
        var creds = TestCredentialStore.Get(tenantId, "nuvemshop")!;
        (await authed.PutAsJsonAsync(new Uri("/api/v1/tenants/me/connections/nuvemshop", UriKind.Relative), new { accountId = creds.AccountId, webhookSecret = creds.WebhookSecret })).EnsureSuccessStatusCode();
        using var client = infrastructure.Api.CreateClient();

        var webhookId = await PostIdOnlyAsync(client, tenantId, NewExternalId());
        var state = await WaitForAsync(infrastructure, webhookId, s => s.Status == "Failed");

        state.LastError.Should().Contain("token");
    }

    private sealed record AcceptedBody(Guid WebhookId);
}
