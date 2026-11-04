using Npgsql;
using RiskGuard.IntegrationTests.Infrastructure;

namespace RiskGuard.IntegrationTests.Ingestion;

internal static class IngestionTestHarness
{
    public sealed record RawState(string Status, int Attempts, string? LastError);

    public static async Task<RawState> ReadRawAsync(InfrastructureFixture infrastructure, Guid webhookId)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"Status\", \"Attempts\", \"LastError\" FROM ingestion.\"RawWebhooks\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", webhookId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("o raw webhook deve existir");
        return new RawState(reader.GetString(0), reader.GetInt32(1), await reader.IsDBNullAsync(2) ? null : reader.GetString(2));
    }

    public static async Task<RawState> WaitForAsync(InfrastructureFixture infrastructure, Guid webhookId, Func<RawState, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        RawState? state = null;
        while (DateTime.UtcNow < deadline)
        {
            state = await ReadRawAsync(infrastructure, webhookId);
            if (condition(state))
            {
                return state;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"Webhook {webhookId} nao atingiu o estado esperado: {state}");
    }

    public static async Task<long> CountRawAsync(InfrastructureFixture infrastructure, Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM ingestion.\"RawWebhooks\" WHERE \"TenantId\" = @t", connection);
        command.Parameters.AddWithValue("t", tenantId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
