using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.Extensions.Options;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;

namespace RiskGuard.Infrastructure.Jobs;

/// <summary>
/// Instala o schema do Hangfire com a credencial ADMINISTRATIVA (passo de migrations) e concede ao papel de jobs, e so a ele,
/// as permissoes de runtime no schema. Assim a API roda com um papel sem privilegio de dono e sem acesso aos dados dos modulos.
/// </summary>
internal sealed class HangfireSchemaMigrator(IOptions<PostgresOptions> postgres, IOptions<HangfireOptions> hangfire) : IDatabaseMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var admin = postgres.Value.MigrationConnectionString;
        var schema = hangfire.Value.SchemaName; // validado por regex nas options: seguro entre aspas
        var role = postgres.Value.JobsRole; // idem

        var storageOptions = new PostgreSqlStorageOptions { SchemaName = schema, PrepareSchemaIfNecessary = true };

        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // O construtor instala o schema e as tabelas (idempotente) na conexao administrativa recebida.
        _ = new PostgreSqlStorage(new ExistingNpgsqlConnectionFactory(connection, storageOptions), storageOptions);

        await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @role", connection))
        {
            exists.Parameters.AddWithValue("role", role);
            if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                return; // papel nao provisionado (ambiente local simples): o Hangfire usara a conexao de fallback
            }
        }

#pragma warning disable CA2100 // schema e papel vem de opcoes validadas por regex ^[a-z_][a-z0-9_]*$ e entram entre aspas
        await using var grant = new NpgsqlCommand(
            $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "{schema}" TO "{role}";
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA "{schema}" TO "{role}";
            """,
            connection);
#pragma warning restore CA2100
        await grant.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
