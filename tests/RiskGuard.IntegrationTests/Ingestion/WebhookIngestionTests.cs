using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.IntegrationTests.Tenant;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Ingestion;

/// <summary>Criterio de aceite 1 (SDD 6): recebimento rapido, rate limiting, raw payload, Hangfire e HTTP 202.</summary>
[Collection(InfrastructureCollection.Name)]
public class WebhookIngestionTests(InfrastructureFixture infrastructure)
{
    private static string ShopifyOrder(string externalId, string email = "fraude@teste.com", string total = "199.90") => $$"""
        { "id": {{externalId}}, "email": "{{email}}", "created_at": "2026-03-01T10:15:30-03:00", "total_price": "{{total}}" }
        """;

    [Fact]
    public async Task Valid_webhook_returns_202_stores_raw_payload_and_is_processed_by_hangfire()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var externalId = NewExternalId();

        var response = await Post(client, "shopify", tenantId, ShopifyOrder(externalId), ("X-Shopify-Topic", "orders/create"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var webhookId = (await response.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId;

        // RF03: payload original e headers (sem segredos) persistidos para auditoria.
        var raw = await ReadRawWebhookAsync(webhookId);
        raw.TenantId.Should().Be(tenantId);
        raw.Platform.Should().Be("shopify");
        raw.Payload.Should().Contain(externalId).And.Contain("fraude@teste.com");
        raw.Headers.Should().Contain("X-Shopify-Topic");

        // Processamento assincrono: raw -> Order normalizado + evento de dominio.
        await WaitForStatusAsync(webhookId, "Processed");
        var order = await ReadOrderAsync(tenantId, externalId);
        order.Should().NotBeNull();
        order!.Value.Email.Should().Be("fraude@teste.com");
        order.Value.Total.Should().Be(199.90m);
        order.Value.Platform.Should().Be("shopify");

        var captured = infrastructure.Api.Services.GetRequiredService<OrderIngestedCapture>().Events;
        captured.Should().Contain(e => e.TenantId == tenantId && e.ExternalOrderId == externalId && e.CustomerEmail == "fraude@teste.com");
    }

    [Fact]
    public async Task Authorization_and_cookie_headers_are_not_stored()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var response = await Post(client, "shopify", tenantId, ShopifyOrder(NewExternalId()),
            ("Authorization", "Bearer super-secret"), ("Cookie", "sid=abc"));
        var webhookId = (await response.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId;

        var raw = await ReadRawWebhookAsync(webhookId);
        raw.Headers.Should().NotContainEquivalentOf("super-secret").And.NotContainEquivalentOf("sid=abc");
    }

    [Fact]
    public async Task Nuvemshop_payloads_flow_through_the_same_pipeline()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var externalId = NewExternalId();
        var body = $$"""{ "id": {{externalId}}, "contact_email": "Comprador@Loja.com", "total": "75.00", "created_at": "2026-03-01T09:11:51+0000" }""";

        var response = await Post(client, "nuvemshop", tenantId, body);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await WaitForStatusAsync((await response.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId, "Processed");
        (await ReadOrderAsync(tenantId, externalId, "nuvemshop"))!.Value.Email.Should().Be("comprador@loja.com");
    }

    [Fact]
    public async Task Redelivery_of_the_same_order_is_idempotent()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var externalId = NewExternalId();

        var first = await Post(client, "shopify", tenantId, ShopifyOrder(externalId));
        var second = await Post(client, "shopify", tenantId, ShopifyOrder(externalId));

        await WaitForStatusAsync((await first.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId, "Processed");
        await WaitForStatusAsync((await second.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId, "Processed");
        (await CountAsync("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"TenantId\" = @t AND \"ExternalOrderId\" = @e",
            ("t", tenantId), ("e", externalId))).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM ingestion.\"RawWebhooks\" WHERE \"TenantId\" = @t", ("t", tenantId))).Should().Be(2);
    }

    [Fact]
    public async Task Syntactically_valid_json_that_the_parser_rejects_is_audited_and_marked_failed_without_retry()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var response = await Post(client, "shopify", tenantId, """{ "unexpected": "shape" }""");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted); // a plataforma nao deve reenviar: guardamos para auditoria
        var webhookId = (await response.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId;
        await WaitForStatusAsync(webhookId, "Failed");
        (await ReadRawWebhookAsync(webhookId)).Error.Should().NotBeNullOrWhiteSpace();
        (await CountAsync("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"TenantId\" = @t", ("t", tenantId))).Should().Be(0);
    }

    [Fact]
    public async Task Tenant_snapshot_is_cached_in_redis_after_the_first_webhook()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        await Post(client, "shopify", tenantId, ShopifyOrder(NewExternalId()));

        var cache = infrastructure.Api.Services.GetRequiredService<ICacheService>();
        var cached = await cache.GetAsync<RiskGuard.Modules.Tenant.Application.Queries.TenantSnapshotCacheEntry>($"tenant:{tenantId}:snapshot");
        cached.Should().NotBeNull();
        cached!.Snapshot!.HighRiskThreshold.Should().Be(80);
    }

    [Fact]
    public async Task Unknown_inactive_tenant_or_unsupported_platform_return_404()
    {
        var (inactiveId, _) = await RegisterAsync(infrastructure.Api);
        await ExecuteAsAdminAsync(infrastructure, "UPDATE tenant.\"Tenants\" SET \"IsActive\" = false WHERE \"Id\" = @id", ("id", inactiveId));
        var (activeId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        (await Post(client, "shopify", Guid.NewGuid(), ShopifyOrder(NewExternalId()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Post(client, "shopify", inactiveId, ShopifyOrder(NewExternalId()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Post(client, "magento", activeId, ShopifyOrder(NewExternalId()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public async Task Empty_or_malformed_json_returns_400_and_stores_nothing(string body)
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var response = await Post(client, "shopify", tenantId, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync("SELECT count(*) FROM ingestion.\"RawWebhooks\" WHERE \"TenantId\" = @t", ("t", tenantId))).Should().Be(0);
    }

    [Fact]
    public async Task Oversized_body_returns_413()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var huge = "{ \"pad\": \"" + new string('x', 1024 * 1024 + 10) + "\" }";

        var response = await Post(client, "shopify", tenantId, huge);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Per_tenant_rate_limit_returns_429_with_retry_after_and_does_not_affect_other_tenants()
    {
        await using var factory = new ApiFactory(infrastructure, webhookTenantPermitLimit: 2);
        var (noisy, _) = await RegisterAsync(factory);
        var (quiet, _) = await RegisterAsync(factory);
        using var client = factory.CreateClient();

        (await Post(client, "shopify", noisy, ShopifyOrder(NewExternalId()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Post(client, "shopify", noisy, ShopifyOrder(NewExternalId()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var limited = await Post(client, "shopify", noisy, ShopifyOrder(NewExternalId()));

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await Post(client, "shopify", quiet, ShopifyOrder(NewExternalId()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Ingestion_endpoint_stays_well_under_the_200ms_budget_on_the_warm_path()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        await Post(client, "shopify", tenantId, ShopifyOrder(NewExternalId())); // aquece cache/JIT

        var timings = new List<double>();
        for (var i = 0; i < 15; i++)
        {
            var body = ShopifyOrder(NewExternalId());
            var sw = Stopwatch.StartNew();
            var response = await Post(client, "shopify", tenantId, body);
            sw.Stop();
            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
            timings.Add(sw.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        timings[timings.Count / 2].Should().BeLessThan(200, "RNF03: mediana do endpoint de ingestao < 200 ms");
    }

    [Fact]
    public async Task Ingestion_tables_enforce_row_level_security_for_the_application_role()
    {
        var (tenantA, _) = await RegisterAsync(infrastructure.Api);
        var (tenantB, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var a = await Post(client, "shopify", tenantA, ShopifyOrder(NewExternalId()));
        await WaitForStatusAsync((await a.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId, "Processed");

        await using var connection = new NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();

        (await Scalar(connection, "SELECT count(*) FROM ingestion.\"RawWebhooks\"")).Should().Be(0);
        (await Scalar(connection, "SELECT count(*) FROM ingestion.\"Orders\"")).Should().Be(0);

        await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", connection);
        set.Parameters.AddWithValue("t", tenantB.ToString());
        await set.ExecuteScalarAsync();
        (await Scalar(connection, "SELECT count(*) FROM ingestion.\"RawWebhooks\"")).Should().Be(0); // B nao enxerga dados de A
    }

    // ---- helpers ----

    private static string NewExternalId() => Random.Shared.NextInt64(1_000_000_000, long.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Task<HttpResponseMessage> Post(HttpClient client, string platform, Guid tenantId, string body, params (string Name, string Value)[] headers) =>
        WebhookTestClient.PostAsync(client, platform, tenantId, body, headers: headers);

    private async Task WaitForStatusAsync(Guid webhookId, string status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        string? current = null;
        while (DateTime.UtcNow < deadline)
        {
            current = (await ReadRawWebhookAsync(webhookId)).Status;
            if (current == status)
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"Webhook {webhookId} ficou em '{current}', esperado '{status}'.");
    }

    private async Task<RawRow> ReadRawWebhookAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"TenantId\", \"Platform\", \"Headers\"::text, \"Payload\"::text, \"Status\", \"LastError\" FROM ingestion.\"RawWebhooks\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("o raw webhook deve existir");
        return new RawRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), await reader.IsDBNullAsync(5) ? null : reader.GetString(5));
    }

    private async Task<(string Email, decimal Total, string Platform)?> ReadOrderAsync(Guid tenantId, string externalId, string platform = "shopify")
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"CustomerEmail\", \"TotalAmount\", \"Platform\" FROM ingestion.\"Orders\" WHERE \"TenantId\" = @t AND \"ExternalOrderId\" = @e AND \"Platform\" = @p", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("e", externalId);
        command.Parameters.AddWithValue("p", platform);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetString(0), reader.GetDecimal(1), reader.GetString(2)) : null;
    }

    private async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed record AcceptedBody(Guid WebhookId);

    private sealed record RawRow(Guid TenantId, string Platform, string Headers, string Payload, string Status, string? Error);
}
