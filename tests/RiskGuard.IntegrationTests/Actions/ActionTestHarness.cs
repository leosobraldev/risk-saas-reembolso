using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Actions;

internal static class ActionTestHarness
{
    /// <summary>Liga o bloqueio automatico (e opcionalmente muda o limite) ANTES do primeiro webhook, quando o cache do tenant ainda esta vazio.</summary>
    public static Task ConfigureTenantAsync(InfrastructureFixture infrastructure, Guid tenantId, bool autoBlock, int threshold = 80) =>
        ExecuteAsAdminAsync(
            infrastructure,
            "UPDATE tenant.\"TenantSettings\" SET \"AutoBlockEnabled\" = @a, \"HighRiskThreshold\" = @h WHERE \"TenantId\" = @t",
            ("a", autoBlock), ("h", threshold), ("t", tenantId));

    public static async Task<ActionRow?> FindByExternalIdAsync(InfrastructureFixture infrastructure, Guid tenantId, string externalOrderId)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\", \"EvaluationId\", \"Status\", \"Type\", \"Attempts\", \"LastError\", \"Score\", \"Platform\" FROM action.\"OrderActions\" WHERE \"TenantId\" = @t AND \"ExternalOrderId\" = @e", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("e", externalOrderId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new ActionRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), await reader.IsDBNullAsync(5) ? null : reader.GetString(5), reader.GetInt32(6), reader.GetString(7))
            : null;
    }

    public static async Task<ActionRow> WaitForStatusAsync(InfrastructureFixture infrastructure, Guid tenantId, string externalOrderId, string status) =>
        await WaitForStatusAsync(infrastructure, infrastructure.Api, tenantId, externalOrderId, status);

    public static async Task<ActionRow> WaitForStatusAsync(InfrastructureFixture infrastructure, ApiFactory factory, Guid tenantId, string externalOrderId, string status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        ActionRow? row = null;
        while (DateTime.UtcNow < deadline)
        {
            row = await FindByExternalIdAsync(infrastructure, tenantId, externalOrderId);
            if (row?.Status == status)
            {
                return row;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException(
            $"Acao do pedido {externalOrderId} ficou em '{row?.Status ?? "inexistente"}' (erro: {row?.LastError}), esperado '{status}'. Hangfire: {DescribeHangfire(factory)}");
    }

    public static async Task<long> CountActionsAsync(InfrastructureFixture infrastructure, Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM action.\"OrderActions\" WHERE \"TenantId\" = @t", connection);
        command.Parameters.AddWithValue("t", tenantId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Resumo do estado do Hangfire do host (jobs falhos, agendados, em execucao) para diagnosticar timeouts.</summary>
    public static string DescribeHangfire(ApiFactory factory)
    {
        var api = factory.Services.GetRequiredService<Hangfire.JobStorage>().GetMonitoringApi();
        var stats = api.GetStatistics();
        var failed = api.FailedJobs(0, 3).Select(j => $"[{j.Value.Job?.Type.Name}.{j.Value.Job?.Method.Name}: {j.Value.ExceptionType}: {j.Value.ExceptionMessage}]");
        var scheduled = api.ScheduledJobs(0, 3).Select(j => $"[{j.Value.Job?.Type.Name} em {j.Value.EnqueueAt:T}]");
        return $"enqueued={stats.Enqueued} processing={stats.Processing} scheduled={stats.Scheduled} failed={stats.Failed} succeeded={stats.Succeeded} servers={stats.Servers}; falhas={string.Join(' ', failed)}; agendados={string.Join(' ', scheduled)}";
    }

    public static FakePlatformsHandler Fake(ApiFactory factory) => factory.Services.GetRequiredService<FakePlatformsHandler>();

    public sealed record ActionRow(Guid Id, Guid EvaluationId, string Status, string Type, int Attempts, string? LastError, int Score, string Platform);
}
