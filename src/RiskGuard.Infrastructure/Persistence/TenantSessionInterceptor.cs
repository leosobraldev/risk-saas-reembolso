using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Infrastructure.Persistence;

/// <summary>
/// A cada conexao aberta grava o tenant corrente na sessao do PostgreSQL (<c>app.current_tenant</c>),
/// que e o que as policies de Row-Level Security leem. Sobrescreve sempre (inclusive com vazio) para que uma
/// conexao reaproveitada do pool nunca carregue o tenant de outra requisicao. Sem tenant => RLS nega tudo.
/// </summary>
internal sealed class TenantSessionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public const string SettingName = "app.current_tenant";

    private const string Sql = "SELECT set_config('" + SettingName + "', @tenant, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenantContext.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);

        return command;
    }
}
