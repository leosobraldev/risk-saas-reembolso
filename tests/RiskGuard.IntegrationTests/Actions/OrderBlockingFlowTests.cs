using System.Net;
using System.Net.Http.Json;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Action.Application.Blockers;
using static RiskGuard.IntegrationTests.Actions.ActionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Actions;

/// <summary>
/// Criterio de aceite 3 (SDD 6), ponta a ponta: webhook assinado -> risco 90 -> ActionModule -> API da plataforma
/// (Shopify/Nuvemshop falsas, no formato das APIs reais) com as credenciais do lojista.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class OrderBlockingFlowTests(InfrastructureFixture infrastructure)
{
    private ApiFactory Api => infrastructure.Api;

    [Fact]
    public async Task Acceptance_scenario_score_90_with_threshold_80_places_the_shopify_order_on_hold()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true);
        await SeedRefundsAsync(infrastructure, tenantId, "fraude@teste.com", 4); // +40, email +50 => 90
        using var client = Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        var risk = await WaitForRiskEventAsync(Api, tenantId, externalId);
        var action = await WaitForStatusAsync(infrastructure, tenantId, externalId, "Succeeded");

        risk.Score.Should().Be(90);
        action.Type.Should().Be("Block");
        action.Score.Should().Be(90);
        action.Platform.Should().Be("shopify");
        action.EvaluationId.Should().Be(risk.EvaluationId);
        action.Attempts.Should().Be(0);

        // o pedido ficou realmente em hold na (falsa) Shopify, com o motivo do RiskGuard
        Fake(Api).ShopifyStatuses(externalId).Should().Equal("on_hold");
        Fake(Api).ShopifyHoldNotes(externalId).Should().ContainSingle().Which.Should().StartWith(ShopifyOrderBlocker.NotePrefix);

        // chamadas reais: lista as fulfillment orders e faz hold, com o token DO LOJISTA
        var calls = Fake(Api).CallsFor(externalId);
        calls.Select(c => c.Method).Should().Equal("GET", "POST");
        calls[0].Url.AbsolutePath.Should().Be($"/admin/api/{ShopifyOrderBlocker.DefaultApiVersion}/orders/{externalId}/fulfillment_orders.json");
        calls[1].Url.AbsolutePath.Should().EndWith("/hold.json");
        calls[1].Body.Should().Contain("high_risk_of_fraud");
        calls.Should().OnlyContain(c => c.Token == TestCredentialStore.Get(tenantId, "shopify")!.AccessToken);
    }

    [Fact]
    public async Task High_risk_with_autoblock_disabled_is_recorded_but_never_calls_the_platform()
    {
        var (tenantId, _) = await RegisterAsync(Api); // AutoBlockEnabled = false (padrao)
        await SeedRefundsAsync(infrastructure, tenantId, "fraude@teste.com", 4);
        using var client = Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        await WaitForRiskEventAsync(Api, tenantId, externalId);
        var action = await WaitForStatusAsync(infrastructure, tenantId, externalId, "Skipped");
        await Task.Delay(1500); // um job indevido teria tempo de chamar a plataforma

        action.LastError.Should().Contain("auto-block");
        Fake(Api).CallsFor(externalId).Should().BeEmpty();
        Fake(Api).ShopifyStatuses(externalId).Should().Equal("open");
    }

    [Fact]
    public async Task Score_at_or_below_the_threshold_creates_no_action_and_no_call()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true);
        using var client = Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com"); // 50 <= 80
        var risk = await WaitForRiskEventAsync(Api, tenantId, externalId);
        await Task.Delay(1500);

        risk.Score.Should().Be(50);
        (await FindByExternalIdAsync(infrastructure, tenantId, externalId)).Should().BeNull();
        Fake(Api).CallsFor(externalId).Should().BeEmpty();
    }

    [Fact]
    public async Task The_tenants_own_threshold_decides_whether_to_block()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        using var client = Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com"); // 50 > 40
        await WaitForStatusAsync(infrastructure, tenantId, externalId, "Succeeded");

        Fake(Api).ShopifyStatuses(externalId).Should().Equal("on_hold");
    }

    [Fact]
    public async Task A_rejection_by_the_platform_is_a_permanent_failure_without_retries()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        var externalId = NewExternalId();
        Fake(Api).Script(externalId, HttpStatusCode.NotFound);
        using var client = Api.CreateClient();

        await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com", externalId: externalId);
        var action = await WaitForStatusAsync(infrastructure, tenantId, externalId, "Failed");
        await Task.Delay(1500);

        action.LastError.Should().Contain("404");
        action.Attempts.Should().Be(0); // falha permanente: nao e tentativa de retry
        Fake(Api).CallsFor(externalId).Should().ContainSingle();
    }

    [Fact]
    public async Task A_shop_with_a_hold_from_another_origin_already_in_place_is_still_reported_as_blocked()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        var externalId = NewExternalId();
        Fake(Api).AddMerchantHold(externalId, "Aguardando confirmacao do cliente");
        using var client = Api.CreateClient();

        await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com", externalId: externalId);
        await WaitForStatusAsync(infrastructure, tenantId, externalId, "Succeeded");

        // o pedido ja estava em hold: nada foi sobrescrito nem duplicado
        Fake(Api).ShopifyHoldNotes(externalId).Should().Equal("Aguardando confirmacao do cliente");
        Fake(Api).CallsFor(externalId).Should().ContainSingle(c => c.Method == "GET");
    }

    [Fact]
    public async Task Nuvemshop_has_no_hold_so_the_order_is_flagged_with_an_internal_note_and_never_cancelled()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        var externalId = NewExternalId();
        Fake(Api).AddNuvemshopOrder(externalId, "fraude@teste.com", "100.00", note: "Cliente pediu presente");
        using var client = Api.CreateClient();
        var body = $$"""{ "id": {{externalId}}, "contact_email": "fraude@teste.com", "total": "100.00", "created_at": "2026-03-01T09:11:51+0000" }""";

        (await WebhookTestClient.PostAsync(client, "nuvemshop", tenantId, body)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var action = await WaitForStatusAsync(infrastructure, tenantId, externalId, "Succeeded");

        action.Platform.Should().Be("nuvemshop");
        action.Type.Should().Be("Flag");
        var note = Fake(Api).NuvemshopNote(externalId)!;
        note.Should().StartWith("Cliente pediu presente").And.Contain(NuvemshopOrderBlocker.Marker);
        Fake(Api).CallsFor(externalId).Select(c => c.Method).Should().Equal("GET", "PUT"); // nenhuma chamada de cancel/close
        Fake(Api).CallsFor(externalId).Should().OnlyContain(c => c.Token == $"bearer {TestCredentialStore.Get(tenantId, "nuvemshop")!.AccessToken}");
    }

    [Fact]
    public async Task A_shop_connected_without_an_access_token_fails_permanently_with_a_clear_reason()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        using var client = Api.CreateClient();
        using var authed = Api.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessTokenFor(email));
        var creds = TestCredentialStore.Get(tenantId, "shopify")!;
        (await authed.PutAsJsonAsync(new Uri("/api/v1/tenants/me/connections/shopify", UriKind.Relative), new { accountId = creds.AccountId, webhookSecret = creds.WebhookSecret }))
            .EnsureSuccessStatusCode(); // reconecta SEM token

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        var action = await WaitForStatusAsync(infrastructure, tenantId, externalId, "Failed");

        action.LastError.Should().Contain("token");
        Fake(Api).CallsFor(externalId).Should().BeEmpty();
    }

    [Fact]
    public async Task Redelivery_of_the_same_order_blocks_only_once()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        using var client = Api.CreateClient();
        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        await WaitForStatusAsync(infrastructure, tenantId, externalId, "Succeeded");

        await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com", externalId: externalId); // reentrega
        await Task.Delay(3000); // tempo para o segundo webhook percorrer todo o pipeline

        (await CountActionsAsync(infrastructure, tenantId)).Should().Be(1);
        Fake(Api).CallsFor(externalId).Should().HaveCount(2); // 1 GET + 1 POST (nada repetido)
        Fake(Api).ShopifyHoldNotes(externalId).Should().ContainSingle();
    }

    [Fact]
    public async Task Action_table_enforces_row_level_security_for_the_application_role()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        using var client = Api.CreateClient();
        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        await WaitForStatusAsync(infrastructure, tenantId, externalId, "Succeeded");

        await using var connection = new Npgsql.NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM action.\"OrderActions\"", connection);

        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }
}
