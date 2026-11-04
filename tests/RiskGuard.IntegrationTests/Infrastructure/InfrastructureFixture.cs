using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace RiskGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe PostgreSQL e Redis efemeros (Docker) uma unica vez para todos os testes de integracao.
/// O Testcontainers cria o usuario como superuser (que ignora RLS); por isso tambem criamos o papel
/// <c>riskguard_app</c> (sem superuser/BYPASSRLS), com o qual a API roda de fato, como em producao.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public const string AppRole = "riskguard_app";
    public const string AppRolePassword = "app_role_test_password";
    public const string JobsRole = "riskguard_jobs";
    public const string JobsRolePassword = "jobs_role_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();

    /// <summary>Conexao do dono/superuser: migrations, Hangfire e verificacoes administrativas.</summary>
    public string PostgresConnectionString => _postgres.GetConnectionString();

    /// <summary>Conexao do papel restrito usado pela aplicacao (sujeito a RLS).</summary>
    public string AppConnectionString =>
        new NpgsqlConnectionStringBuilder(PostgresConnectionString)
        {
            Username = AppRole,
            Password = AppRolePassword,
        }.ConnectionString;

    /// <summary>Conexao do papel do Hangfire: so enxerga o schema do proprio Hangfire.</summary>
    public string JobsConnectionString =>
        new NpgsqlConnectionStringBuilder(PostgresConnectionString)
        {
            Username = JobsRole,
            Password = JobsRolePassword,
        }.ConnectionString;

    public string RedisConnectionString => _redis.GetConnectionString();

    private ApiFactory? _api;

    /// <summary>Host padrao da API compartilhado pelos testes que nao precisam de configuracao propria.</summary>
    public ApiFactory Api => _api ??= new ApiFactory(this);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;" +
            $"CREATE ROLE {JobsRole} LOGIN PASSWORD '{JobsRolePassword}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "infrastructure";
}
