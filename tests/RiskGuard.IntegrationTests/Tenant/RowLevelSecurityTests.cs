using Npgsql;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Tenant;

/// <summary>
/// Prova a RLS direto no banco, conectado como o papel restrito da aplicacao (nao-superuser),
/// independente de qualquer filtro do EF Core: defesa em profundidade.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class RowLevelSecurityTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Without_a_tenant_in_the_session_no_rows_are_visible()
    {
        await RegisterAsync(infrastructure.Api);

        await using var connection = await OpenAppConnectionAsync();

        (await ScalarAsync(connection, "SELECT count(*) FROM tenant.\"TenantSettings\"")).Should().Be(0);
    }

    [Fact]
    public async Task With_a_tenant_in_the_session_only_its_rows_are_visible()
    {
        var (tenantA, _) = await RegisterAsync(infrastructure.Api);
        await RegisterAsync(infrastructure.Api);

        await using var connection = await OpenAppConnectionAsync();
        await SetTenantAsync(connection, tenantA);

        (await ScalarAsync(connection, "SELECT count(*) FROM tenant.\"TenantSettings\"")).Should().Be(1);
        (await ScalarAsync(connection, $"SELECT count(*) FROM tenant.\"TenantSettings\" WHERE \"TenantId\" = '{tenantA}'")).Should().Be(1);
    }

    [Fact]
    public async Task A_tenant_cannot_write_rows_for_another_tenant()
    {
        var (tenantA, _) = await RegisterAsync(infrastructure.Api);
        var (tenantB, _) = await RegisterAsync(infrastructure.Api);

        await using var connection = await OpenAppConnectionAsync();
        await SetTenantAsync(connection, tenantA);

        // UPDATE em linha de outro tenant: RLS esconde a linha, 0 linhas afetadas.
        await using var update = new NpgsqlCommand(
            "UPDATE tenant.\"TenantSettings\" SET \"HighRiskThreshold\" = 1 WHERE \"TenantId\" = @other", connection);
        update.Parameters.AddWithValue("other", tenantB);
        (await update.ExecuteNonQueryAsync()).Should().Be(0);

        // INSERT para outro tenant: WITH CHECK rejeita.
        await using var insert = new NpgsqlCommand(
            "INSERT INTO tenant.\"TenantSettings\" (\"TenantId\", \"HighRiskThreshold\", \"AutoBlockEnabled\") VALUES (@other, 1, true)", connection);
        insert.Parameters.AddWithValue("other", Guid.NewGuid());
        var act = async () => await insert.ExecuteNonQueryAsync();
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().BeOneOf("42501", "23503");
    }

    [Fact]
    public async Task The_application_role_is_not_privileged()
    {
        await using var connection = await OpenAppConnectionAsync();

        (await ScalarAsync(connection, "SELECT count(*) FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolbypassrls)"))
            .Should().Be(0);
    }

    private async Task<NpgsqlConnection> OpenAppConnectionAsync()
    {
        var connection = new NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, Guid tenantId)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", connection);
        command.Parameters.AddWithValue("t", tenantId.ToString());
        await command.ExecuteScalarAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
