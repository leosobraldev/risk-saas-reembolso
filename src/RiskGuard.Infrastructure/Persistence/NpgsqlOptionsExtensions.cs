using Microsoft.EntityFrameworkCore;

namespace RiskGuard.Infrastructure.Persistence;

public static class NpgsqlOptionsExtensions
{
    /// <summary>Configuracao padrao de Npgsql dos modulos: historico de migrations no schema do modulo e retry (RNF04).</summary>
    public static DbContextOptionsBuilder UseRiskGuardNpgsql(
        this DbContextOptionsBuilder builder,
        string connectionString,
        string schema)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        return builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
            npgsql.EnableRetryOnFailure(maxRetryCount: 3);
        });
    }
}
