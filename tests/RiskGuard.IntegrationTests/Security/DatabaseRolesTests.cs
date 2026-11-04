using System.Diagnostics;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Security;

/// <summary>
/// Privilegio minimo no banco: o dono (migrations) nunca roda na API; o Hangfire tem papel proprio que so enxerga o proprio schema;
/// o papel da aplicacao nao enxerga o Hangfire. Quem comprometer a API nao herda a credencial que ignora a RLS.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class DatabaseRolesTests(InfrastructureFixture infrastructure)
{
    private static async Task<string?> TryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        try
        {
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    [Fact]
    public async Task The_jobs_role_cannot_read_or_change_any_module_data_nor_create_anything()
    {
        _ = infrastructure.Api.CreateClient(); // garante que as migrations rodaram

        foreach (var table in new[] { "tenant.\"Tenants\"", "tenant.\"PlatformConnections\"", "ingestion.\"RawWebhooks\"", "ingestion.\"Orders\"", "risk.\"RiskEvaluations\"", "action.\"OrderActions\"" })
        {
            (await TryAsync(infrastructure.JobsConnectionString, $"SELECT 1 FROM {table}")).Should().Be("42501", $"o papel de jobs nao pode ler {table}");
        }

        (await TryAsync(infrastructure.JobsConnectionString, "CREATE SCHEMA nao_deve_criar")).Should().Be("42501");
        (await TryAsync(infrastructure.JobsConnectionString, "CREATE TABLE public.nao_deve_criar (id int)")).Should().Be("42501");
    }

    [Fact]
    public async Task The_jobs_role_can_use_only_its_own_hangfire_schema()
    {
        var api = infrastructure.Api;
        _ = api.CreateClient();

        (await TryAsync(infrastructure.JobsConnectionString, $"SELECT count(*) FROM \"{api.HangfireSchema}\".job")).Should().BeNull();
    }

    [Fact]
    public async Task The_application_role_cannot_see_the_hangfire_schema()
    {
        var api = infrastructure.Api;
        _ = api.CreateClient();

        (await TryAsync(infrastructure.AppConnectionString, $"SELECT 1 FROM \"{api.HangfireSchema}\".job")).Should().Be("42501");
    }

    [Fact]
    public async Task The_running_api_connects_to_hangfire_as_the_jobs_role_not_as_the_owner()
    {
        await using var factory = new ApiFactory(infrastructure);
        using var client = factory.CreateClient();
        var enqueued = factory.Services.GetRequiredService<IBackgroundJobClient>().Enqueue(() => Console.Write(string.Empty));
        await Task.Delay(500);

        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE usename = @u", connection);
        command.Parameters.AddWithValue("u", InfrastructureFixture.JobsRole);

        ((long)(await command.ExecuteScalarAsync())!).Should().BeGreaterThan(0);
        enqueued.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Production_topology_migrate_step_then_an_api_without_the_owner_credential_works_end_to_end()
    {
        var database = $"topology_{Guid.NewGuid():N}";
        await ExecuteAsAdminAsync(infrastructure, $"CREATE DATABASE {database}");
        var schema = $"hangfire_{Guid.NewGuid():N}";

        // 1) passo de deploy: a MESMA API em modo --migrate, com a credencial de dono, aplica tudo e encerra
        var exit = await RunMigrateAsync(database, schema);
        exit.Should().Be(0, "o modo --migrate deve aplicar as migrations e sair com sucesso");

        // 2) a API sobe SEM credencial de dono: so o papel da aplicacao e o de jobs
        await using var factory = new ApiFactory(infrastructure, database: database, leastPrivilegeTopology: true, extraSettings: new Dictionary<string, string> { ["Hangfire:SchemaName"] = schema });
        var (tenantId, email) = await RegisterAsync(factory);
        using var client = await AuthenticatedClientAsync(factory, email);

        var me = await client.GetAsync(new Uri("/api/v1/tenants/me", UriKind.Relative));
        me.EnsureSuccessStatusCode();

        // o Hangfire (papel de jobs, sem DDL) enfileira e executa jobs normalmente
        var jobClient = factory.Services.GetRequiredService<IBackgroundJobClient>();
        var jobId = jobClient.Enqueue<ProbeJob>(job => job.Run());
        var storage = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? state = null;
        while (DateTime.UtcNow < deadline && state != "Succeeded")
        {
            state = storage.JobDetails(jobId)?.History.FirstOrDefault()?.StateName;
            await Task.Delay(250);
        }

        state.Should().Be("Succeeded");
        tenantId.Should().NotBeEmpty();
    }

    private async Task<int> RunMigrateAsync(string database, string schema)
    {
        var dll = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet", $"\"{dll}\" --migrate")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        string Db(string cs) => new NpgsqlConnectionStringBuilder(cs) { Database = database }.ConnectionString;

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Postgres__ConnectionString"] = Db(infrastructure.AppConnectionString);
        start.Environment["Postgres__AdminConnectionString"] = Db(infrastructure.PostgresConnectionString);
        start.Environment["Postgres__JobsConnectionString"] = Db(infrastructure.JobsConnectionString);
        start.Environment["Hangfire__SchemaName"] = schema;
        start.Environment["Redis__ConnectionString"] = infrastructure.RedisConnectionString;
        start.Environment["Identity__Audience"] = TestIdentity.Audience;
        start.Environment["Identity__Auth0__Domain"] = "riskguard-tests.us.auth0.com";
        start.Environment["Identity__Auth0__ClientId"] = "test-client-id";
        start.Environment["Identity__Auth0__ClientSecret"] = "test-client-secret";
        start.Environment["Security__EncryptionKey"] = "dGVzdC1vbmx5LWFlcy1rZXktMzItYnl0ZXMtISEhISE=";

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(cts.Token);
        if (process.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"--migrate falhou ({process.ExitCode}): {await output} {await error}");
        }

        return process.ExitCode;
    }

    public sealed class ProbeJob
    {
        public void Run() => Console.Write(string.Empty);
    }
}
