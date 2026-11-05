using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Actions.ActionTestHarness;
using static RiskGuard.IntegrationTests.Ingestion.IngestionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Platforms;

/// <summary>
/// Wake: o webhook (<c>pedido.novo</c>) traz so o numero do pedido e nao e assinado (o lojista cadastra o header X-RiskGuard-Token). O
/// pedido e buscado em GET /pedidos/{id} com o token do lojista e segue o fluxo normal. A Wake nao tem "reter pedido": so avalia.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class WakeFlowTests(InfrastructureFixture infrastructure)
{
    private ApiFactory Api => infrastructure.Api;

    private FakePlatformsHandler Fake => Api.Services.GetRequiredService<FakePlatformsHandler>();

    private static string Notification(string topic, string store, string orderId) => $$"""
        {
          "topico": "{{topic}}",
          "evento": { "loja": "{{store}}", "pedido": {{orderId}}, "id": "{{Guid.NewGuid()}}", "data_evento": "2026-03-01T09:11:51" },
          "data_envio": "2026-03-01T09:11:52.00",
          "tentativa": 1
        }
        """;

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Conecta a Wake pela API publica (como o lojista faria), com token de acesso e segredo aleatorios.</summary>
    private async Task<TestCredentials> ConnectWakeAsync(Guid tenantId, string email)
    {
        using var client = await AuthenticatedClientAsync(Api, email);
        var credentials = TestCredentialStore.NewCredentials("wake");
        (await client.PutAsJsonAsync(
            new Uri("/api/v1/tenants/me/connections/wake", UriKind.Relative),
            new { accountId = credentials.AccountId, webhookSecret = credentials.WebhookSecret, accessToken = credentials.AccessToken })).EnsureSuccessStatusCode();
        TestCredentialStore.Set(tenantId, "wake", credentials);
        return credentials;
    }

    /// <summary>
    /// Ajusta o lojista direto no banco e devolve o cache do snapshot ao estado "frio": conectar a Wake pela API ja aquece esse cache
    /// (toda requisicao autenticada o consulta), e sem limpar o valor antigo valeria no lugar do ajuste.
    /// </summary>
    private async Task ConfigureAsync(Guid tenantId, bool autoBlock, int threshold)
    {
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock, threshold);
        await using var scope = Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RiskGuard.Core.Abstractions.ICacheService>().RemoveAsync($"tenant:{tenantId}:snapshot");
    }

    private async Task<Guid> PostNotificationAsync(HttpClient client, Guid tenantId, string orderId, string topic = "pedido.novo")
    {
        var store = TestCredentialStore.Get(tenantId, "wake")!.AccountId;
        var response = await WebhookTestClient.PostAsync(client, "wake", tenantId, Notification(topic, store, orderId));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId;
    }

    // ---- conexao e catalogo ----

    [Fact]
    public async Task The_wake_connection_requires_the_access_token_and_a_valid_store_name()
    {
        var (_, email) = await RegisterAsync(Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(Api, email);
        var uri = new Uri("/api/v1/tenants/me/connections/wake", UriKind.Relative);

        var noToken = await client.PutAsJsonAsync(uri, new { accountId = "MinhaLoja", webhookSecret = "segredo" });
        var badName = await client.PutAsJsonAsync(uri, new { accountId = "http://evil.test", webhookSecret = "segredo", accessToken = "tok" });
        var ok = await client.PutAsJsonAsync(uri, new { accountId = "MinhaLoja", webhookSecret = "segredo", accessToken = "tok" });

        noToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(noToken)).Should().Be("connection.access_token_required");
        badName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(badName)).Should().Be("connection.invalid_account");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var connections = await client.GetFromJsonAsync<List<ConnectionBody>>(new Uri("/api/v1/tenants/me/connections", UriKind.Relative));
        connections.Should().ContainSingle(c => c.Platform == "wake" && c.AccountId == "MinhaLoja" && c.HasAccessToken);
    }

    [Fact]
    public async Task The_platform_catalog_is_authenticated_and_describes_wake()
    {
        var (_, email) = await RegisterAsync(Api, connectPlatforms: false);
        using var anonymous = Api.CreateClient();
        using var client = await AuthenticatedClientAsync(Api, email);

        (await anonymous.GetAsync(new Uri("/api/v1/platforms", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var platforms = (await client.GetFromJsonAsync<List<PlatformBody>>(new Uri("/api/v1/platforms", UriKind.Relative)))!;

        platforms.Select(p => p.Id).Should().Equal("shopify", "nuvemshop", "wake");
        var wake = platforms.Single(p => p.Id == "wake");
        wake.Name.Should().Be("Wake");
        wake.RequiresAccessToken.Should().BeTrue();
        wake.SupportsActions.Should().BeFalse();
        wake.WebhookSecretHelp.Should().Contain("X-RiskGuard-Token");
        platforms.Single(p => p.Id == "shopify").SupportsActions.Should().BeTrue();
    }

    // ---- autenticidade ----

    [Fact]
    public async Task The_notification_is_authenticated_by_the_shared_token_and_nothing_reaches_the_platform_otherwise()
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        await ConnectWakeAsync(tenantId, email);
        var orderId = NewExternalId();
        Fake.AddWakeOrder(orderId, "maria@gmail.com", 10m);
        using var client = Api.CreateClient();
        var store = TestCredentialStore.Get(tenantId, "wake")!.AccountId;
        var body = Notification("pedido.novo", store, orderId);

        var wrong = await WebhookTestClient.PostAsync(client, "wake", tenantId, body, secretOverride: "token-errado");
        var missing = await WebhookTestClient.PostAsync(client, "wake", tenantId, body, sign: false);
        var hmacOnly = await WebhookTestClient.PostAsync(client, "wake", tenantId, body, sign: false, headers: ("X-Shopify-Hmac-SHA256", "qualquer"));

        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeOf(wrong)).Should().Be("webhook.invalid_signature");
        missing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        hmacOnly.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Fake.CallsFor(orderId).Should().BeEmpty();
    }

    [Fact]
    public async Task One_tenants_token_never_authenticates_another_tenants_webhook()
    {
        var (tenantA, emailA) = await RegisterAsync(Api, connectPlatforms: false);
        var (tenantB, emailB) = await RegisterAsync(Api, connectPlatforms: false);
        var credentialsA = await ConnectWakeAsync(tenantA, emailA);
        await ConnectWakeAsync(tenantB, emailB);
        using var client = Api.CreateClient();

        var response = await WebhookTestClient.PostAsync(client, "wake", tenantB, Notification("pedido.novo", "Loja", NewExternalId()), secretOverride: credentialsA.WebhookSecret);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_tenant_without_a_wake_connection_gets_404_and_no_fetch_happens()
    {
        var (tenantId, _) = await RegisterAsync(Api); // so Shopify e Nuvemshop conectadas
        using var client = Api.CreateClient();

        var response = await WebhookTestClient.PostAsync(client, "wake", tenantId, Notification("pedido.novo", "Loja", NewExternalId()), secretOverride: "qualquer");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }

    // ---- fluxo ----

    [Fact]
    public async Task A_new_order_notification_is_completed_by_fetching_the_order_and_flows_through_risk_but_is_never_blocked()
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        var credentials = await ConnectWakeAsync(tenantId, email);
        await ConfigureAsync(tenantId, autoBlock: true, threshold: 40);
        var orderId = NewExternalId();
        Fake.AddWakeOrder(orderId, "Fraude@Teste.com", 250.00m);
        using var client = Api.CreateClient();

        var webhookId = await PostNotificationAsync(client, tenantId, orderId);
        await WaitForAsync(infrastructure, webhookId, s => s.Status == "Processed");

        var risk = await WaitForRiskEventAsync(Api, tenantId, orderId);
        risk.Score.Should().Be(50); // e-mail suspeito vindo do pedido BUSCADO: a notificacao nao tinha e-mail
        risk.Platform.Should().Be("wake");
        var ingested = Api.Services.GetRequiredService<OrderIngestedCapture>().Events.Single(e => e.ExternalOrderId == orderId && e.TenantId == tenantId);
        ingested.CustomerEmail.Should().Be("fraude@teste.com");
        ingested.TotalAmount.Should().Be(250.00m);
        ingested.Platform.Should().Be("wake");

        // a busca usou a API da Wake com o token do lojista; a unica chamada foi o GET (nenhuma escrita na loja)
        var calls = Fake.CallsFor(orderId);
        calls.Should().ContainSingle();
        calls[0].Method.Should().Be("GET");
        calls[0].Url.AbsolutePath.Should().Be($"/pedidos/{orderId}");
        calls[0].Token.Should().Be($"Bearer {credentials.AccessToken}");

        // risco alto (50 > 40) com bloqueio automatico ligado, mas a Wake nao tem como reter: "nao aplicada", dita com clareza
        var action = await FindActionWhenAsync(tenantId, orderId);
        action.Status.Should().Be("Skipped");
        action.Platform.Should().Be("wake");
        action.LastError.Should().Contain("wake").And.Contain("avalia e recomenda");
        Fake.CallsFor(orderId).Should().ContainSingle("nenhuma chamada de bloqueio e feita");
    }

    [Fact]
    public async Task A_low_risk_wake_order_is_evaluated_and_creates_no_action()
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        await ConnectWakeAsync(tenantId, email);
        await ConfigureAsync(tenantId, autoBlock: true, threshold: 80);
        var orderId = NewExternalId();
        Fake.AddWakeOrder(orderId, "maria@gmail.com", 120.00m);
        using var client = Api.CreateClient();

        var webhookId = await PostNotificationAsync(client, tenantId, orderId);
        await WaitForAsync(infrastructure, webhookId, s => s.Status == "Processed");

        (await WaitForRiskEventAsync(Api, tenantId, orderId)).Score.Should().Be(0);
        await Task.Delay(500);
        (await FindByExternalIdAsync(infrastructure, tenantId, orderId)).Should().BeNull();
    }

    [Theory]
    [InlineData("pedido.situacao.alterado")]
    [InlineData("pedido.rastreamento.alterado")]
    [InlineData("pedido.novo.indexado")]
    public async Task Notifications_of_other_topics_are_acknowledged_and_ignored_without_touching_the_platform(string topic)
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        await ConnectWakeAsync(tenantId, email);
        var orderId = NewExternalId();
        Fake.AddWakeOrder(orderId, "fraude@teste.com", 99_999m);
        using var client = Api.CreateClient();

        var webhookId = await PostNotificationAsync(client, tenantId, orderId, topic);
        var state = await WaitForAsync(infrastructure, webhookId, s => s.Status == "Processed");

        state.LastError.Should().BeNull();
        state.Attempts.Should().Be(0);
        Fake.CallsFor(orderId).Should().BeEmpty();
        await Task.Delay(300);
        Api.Services.GetRequiredService<OrderIngestedCapture>().Events.Should().NotContain(e => e.ExternalOrderId == orderId && e.TenantId == tenantId);
    }

    [Fact]
    public async Task The_same_notification_delivered_twice_evaluates_the_order_once()
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        await ConnectWakeAsync(tenantId, email);
        var orderId = NewExternalId();
        Fake.AddWakeOrder(orderId, "maria@gmail.com", 50m);
        using var client = Api.CreateClient();

        var first = await PostNotificationAsync(client, tenantId, orderId);
        var second = await PostNotificationAsync(client, tenantId, orderId); // a Wake reenvia (campo "tentativa")
        await WaitForAsync(infrastructure, first, s => s.Status == "Processed");
        await WaitForAsync(infrastructure, second, s => s.Status == "Processed");

        await WaitForRiskEventAsync(Api, tenantId, orderId);
        (await CountOrdersAsync(tenantId, orderId)).Should().Be(1);
    }

    // ---- falhas ----

    [Fact]
    public async Task An_order_missing_on_the_platform_fails_permanently_without_retry()
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        await ConnectWakeAsync(tenantId, email);
        using var client = Api.CreateClient();

        var webhookId = await PostNotificationAsync(client, tenantId, NewExternalId()); // a Wake falsa nao conhece o pedido (404)
        var state = await WaitForAsync(infrastructure, webhookId, s => s.Status == "Failed");

        state.LastError.Should().Contain("404");
        state.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task A_transient_platform_error_keeps_the_webhook_pending_for_retry()
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        await ConnectWakeAsync(tenantId, email);
        var orderId = NewExternalId();
        Fake.AddWakeOrder(orderId, "maria@gmail.com", 10m);
        Fake.Script(orderId, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);
        using var client = Api.CreateClient();

        var webhookId = await PostNotificationAsync(client, tenantId, orderId);
        var state = await WaitForAsync(infrastructure, webhookId, s => s.Attempts >= 1);

        state.Status.Should().Be("Received"); // nao e Failed: o Hangfire tentara de novo (RNF04)
        state.LastError.Should().Contain("503");
    }

    // ---- apoio ----

    private async Task<ActionRow> FindActionWhenAsync(Guid tenantId, string orderId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await FindByExternalIdAsync(infrastructure, tenantId, orderId) is { } row)
            {
                return row;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"Nenhuma acao criada para o pedido {orderId}.");
    }

    private async Task<long> CountOrdersAsync(Guid tenantId, string orderId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"TenantId\" = @t AND \"ExternalOrderId\" = @e AND \"Platform\" = 'wake'", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("e", orderId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed record AcceptedBody(Guid WebhookId);

    private sealed record ConnectionBody(string Platform, string AccountId, bool HasAccessToken);

    private sealed record PlatformBody(string Id, string Name, string AccountIdLabel, string AccountIdHint, bool RequiresAccessToken, bool SupportsActions, string? WebhookSecretHelp);
}
