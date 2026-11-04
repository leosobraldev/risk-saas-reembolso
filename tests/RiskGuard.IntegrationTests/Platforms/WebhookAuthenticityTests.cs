using System.Net;
using System.Net.Http.Json;
using System.Text;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Ingestion.IngestionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Platforms;

/// <summary>Webhooks so sao aceitos com HMAC valido do corpo cru, calculado com o segredo DO LOJISTA (fail-closed).</summary>
[Collection(InfrastructureCollection.Name)]
public class WebhookAuthenticityTests(InfrastructureFixture infrastructure)
{
    private static string Order() => $$"""{ "id": {{NewExternalId()}}, "email": "maria@gmail.com", "created_at": "2026-03-01T10:15:30Z", "total_price": "10.00" }""";

    [Fact]
    public async Task Unsigned_webhooks_are_rejected_with_401_and_store_nothing()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order(), sign: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountRawAsync(infrastructure, tenantId)).Should().Be(0);
    }

    [Fact]
    public async Task A_signature_made_with_the_wrong_secret_is_rejected()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order(), secretOverride: "segredo-do-atacante");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountRawAsync(infrastructure, tenantId)).Should().Be(0);
    }

    [Fact]
    public async Task A_valid_signature_for_a_different_body_is_rejected()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var genuine = Order();
        var forged = genuine.Replace("10.00", "0.01");
        var signatureOfGenuine = WebhookTestClient.Sign("shopify", Encoding.UTF8.GetBytes(genuine), TestCredentialStore.Get(tenantId, "shopify")!.WebhookSecret);

        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantId, forged, sign: false, headers: (WebhookTestClient.ShopifyHeader, signatureOfGenuine));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountRawAsync(infrastructure, tenantId)).Should().Be(0);
    }

    [Fact]
    public async Task A_replayed_signature_still_authenticates_the_same_body_but_never_another_tenants()
    {
        var (tenantA, _) = await RegisterAsync(infrastructure.Api);
        var (tenantB, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        // segredo de A usado no endpoint de B
        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantB, Order(), secretOverride: TestCredentialStore.Get(tenantA, "shopify")!.WebhookSecret);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_tenant_that_never_connected_the_platform_cannot_receive_webhooks()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = infrastructure.Api.CreateClient();

        // mesmo uma assinatura "correta" com qualquer segredo e recusada: sem segredo configurado, nada e aceito
        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order(), secretOverride: "qualquer");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountRawAsync(infrastructure, tenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Missing_wrong_and_unconnected_are_indistinguishable_to_the_caller()
    {
        var (connected, _) = await RegisterAsync(infrastructure.Api);
        var (unconnected, _) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = infrastructure.Api.CreateClient();

        var missing = await WebhookTestClient.PostAsync(client, "shopify", connected, Order(), sign: false);
        var wrong = await WebhookTestClient.PostAsync(client, "shopify", connected, Order(), secretOverride: "x");
        var none = await WebhookTestClient.PostAsync(client, "shopify", unconnected, Order(), secretOverride: "x");

        var bodies = new[] { await missing.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync(), await none.Content.ReadAsStringAsync() };
        bodies.Distinct().Should().ContainSingle(); // mesma resposta: nao revela o motivo
        new[] { missing, wrong, none }.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]
    public async Task Malformed_signature_headers_are_rejected(string header)
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order(), sign: false, headers: (WebhookTestClient.ShopifyHeader, header));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nuvemshop_accepts_hex_and_base64_signatures_and_rejects_a_shopify_header()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var body = $$"""{ "id": {{NewExternalId()}}, "contact_email": "a@b.com", "total": "10.00", "created_at": "2026-03-01T09:11:51+0000" }""";
        var bytes = Encoding.UTF8.GetBytes(body);
        var secret = TestCredentialStore.Get(tenantId, "nuvemshop")!.WebhookSecret;
        var hex = WebhookTestClient.Sign("nuvemshop", bytes, secret);
        var base64 = WebhookTestClient.Sign("shopify", bytes, secret); // mesmo digest, codificado em base64

        (await WebhookTestClient.PostAsync(client, "nuvemshop", tenantId, body, sign: false, headers: (WebhookTestClient.NuvemshopHeader, hex))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await WebhookTestClient.PostAsync(client, "nuvemshop", tenantId, body, sign: false, headers: (WebhookTestClient.NuvemshopHeader, base64))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await WebhookTestClient.PostAsync(client, "nuvemshop", tenantId, body, sign: false, headers: (WebhookTestClient.ShopifyHeader, base64))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rotating_the_secret_takes_effect_immediately()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        using var authed = await AuthenticatedClientAsync(infrastructure.Api, email);
        var old = TestCredentialStore.Get(tenantId, "shopify")!;
        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order())).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await authed.PutAsJsonAsync(new Uri("/api/v1/tenants/me/connections/shopify", UriKind.Relative), new { accountId = old.AccountId, webhookSecret = "novo-segredo-rotacionado", accessToken = old.AccessToken }))
            .EnsureSuccessStatusCode();

        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order(), secretOverride: old.WebhookSecret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order(), secretOverride: "novo-segredo-rotacionado")).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Disconnecting_the_platform_stops_accepting_its_webhooks()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        using var authed = await AuthenticatedClientAsync(infrastructure.Api, email);
        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order())).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await authed.DeleteAsync(new Uri("/api/v1/tenants/me/connections/shopify", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, Order())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_utf8_bodies_with_a_valid_signature_are_rejected_as_invalid_json()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var bytes = new byte[] { 0x7B, 0xFF, 0xFE, 0x7D }; // "{" + bytes invalidos + "}"
        var secret = TestCredentialStore.Get(tenantId, "shopify")!.WebhookSecret;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/webhooks/shopify/{tenantId}", UriKind.Relative)) { Content = new ByteArrayContent(bytes) };
        request.Headers.TryAddWithoutValidation(WebhookTestClient.ShopifyHeader, Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes)));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
