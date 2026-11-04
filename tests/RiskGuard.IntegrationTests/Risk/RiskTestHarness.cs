using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.Core.Contracts;
using RiskGuard.IntegrationTests.Infrastructure;

namespace RiskGuard.IntegrationTests.Risk;

internal static class RiskTestHarness
{
    public static string NewExternalId() =>
        Random.Shared.NextInt64(1_000_000_000, long.MaxValue).ToString(CultureInfo.InvariantCulture);

    /// <summary>Envia um pedido Shopify pelo endpoint publico e devolve o id externo.</summary>
    public static async Task<string> PostShopifyOrderAsync(HttpClient client, Guid tenantId, string email, string total = "100.00", string? externalId = null)
    {
        externalId ??= NewExternalId();
        var body = $$"""{ "id": {{externalId}}, "email": "{{email}}", "created_at": "2026-03-01T10:15:30Z", "total_price": "{{total}}" }""";

        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantId, body);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return externalId;
    }

    /// <summary>Aguarda o RiskCalculatedEvent do pedido (fluxo completo: webhook -> job -> pedido -> risco).</summary>
    public static async Task<RiskCalculatedEvent> WaitForRiskEventAsync(ApiFactory factory, Guid tenantId, string externalId)
    {
        var capture = factory.Services.GetRequiredService<RiskCalculatedCapture>();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var found = capture.Events.FirstOrDefault(e => e.TenantId == tenantId && e.ExternalOrderId == externalId);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"RiskCalculatedEvent do pedido {externalId} nao foi publicado.");
    }

    public static async Task SeedRefundsAsync(InfrastructureFixture infrastructure, Guid tenantId, string email, int totalRefunds)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO risk.\"CustomerHistories\" (\"Id\", \"TenantId\", \"CustomerEmail\", \"TotalRefunds\") VALUES (@id, @t, @e, @r)", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("e", email);
        command.Parameters.AddWithValue("r", totalRefunds);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> CountAsync(InfrastructureFixture infrastructure, string sql, params (string Name, object Value)[] parameters)
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
}
