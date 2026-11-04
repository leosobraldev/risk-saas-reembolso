using System.ComponentModel.DataAnnotations;

namespace RiskGuard.Infrastructure.Options;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    /// <summary>Conexao da aplicacao: papel restrito (nao-superuser), sujeito a Row-Level Security.</summary>
    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Conexao do DONO do schema: usada SOMENTE para migrations (<c>--migrate</c> ou <see cref="MigrateOnStartup"/>). Em producao a
    /// API nao deve receber esta credencial: quem comprometer a API passaria por cima da RLS.
    /// </summary>
    public string? AdminConnectionString { get; init; }

    /// <summary>
    /// Conexao do armazenamento do Hangfire: papel proprio (<see cref="JobsRole"/>) sem acesso aos dados dos modulos, so ao schema
    /// do Hangfire. Se vazia (dev), cai para a administrativa e, por fim, para <see cref="ConnectionString"/>.
    /// </summary>
    public string? JobsConnectionString { get; init; }

    /// <summary>Papel que recebe as permissoes no schema do Hangfire (concedidas pelo passo de migrations).</summary>
    [RegularExpression("^[a-z_][a-z0-9_]{0,62}$")]
    public string JobsRole { get; init; } = "riskguard_jobs";

    /// <summary>Aplica migrations ao subir a API (dev/testes). Em producao prefira executar como etapa de deploy.</summary>
    public bool MigrateOnStartup { get; init; }

    public string MigrationConnectionString =>
        string.IsNullOrWhiteSpace(AdminConnectionString) ? ConnectionString : AdminConnectionString;

    public string HangfireConnectionString =>
        !string.IsNullOrWhiteSpace(JobsConnectionString) ? JobsConnectionString : MigrationConnectionString;
}

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    public string InstanceName { get; init; } = "riskguard:";
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 1_000_000)]
    public int PermitLimit { get; init; } = 100;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;

    /// <summary>Limite por IP nas rotas de webhook (plataformas enviam muito trafego de poucos IPs).</summary>
    [Range(1, 10_000_000)]
    public int WebhookIpPermitLimit { get; init; } = 3000;

    /// <summary>Limite por tenant na ingestao de webhooks (por janela).</summary>
    [Range(1, 10_000_000)]
    public int WebhookTenantPermitLimit { get; init; } = 600;
}

public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    [Range(1, 100)]
    public int WorkerCount { get; init; } = 5;

    /// <summary>Tentativas automaticas em caso de falha (RNF04).</summary>
    [Range(0, 20)]
    public int MaxRetryAttempts { get; init; } = 5;

    /// <summary>Desligado nos testes de integracao/API-only; ligado por padrao.</summary>
    public bool ServerEnabled { get; init; } = true;

    /// <summary>Schema do armazenamento do Hangfire. Instancias com schemas diferentes nao compartilham filas.</summary>
    [RegularExpression("^[a-z_][a-z0-9_]{0,62}$")]
    public string SchemaName { get; init; } = "hangfire";
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Chave AES-256 em base64 (32 bytes) que cifra segredos de terceiros. Nunca versionar o valor real.</summary>
    [Required]
    public string EncryptionKey { get; init; } = string.Empty;

    public byte[]? TryGetKeyBytes()
    {
        try
        {
            var bytes = Convert.FromBase64String(EncryptionKey);
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Como falar com as APIs das plataformas (valores do servidor; nunca vindos do tenant).</summary>
public sealed class PlatformApisOptions
{
    public const string SectionName = "PlatformApis";

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Algumas APIs (Nuvemshop) exigem User-Agent com o nome do app e um contato; configure o contato real.</summary>
    [Required]
    public string UserAgent { get; init; } = "RiskGuard/1.0";

    /// <summary>Por plataforma (chave = id, ex.: "shopify"). Opcional: sobrescreve a URL real (homologacao, testes).</summary>
    public Dictionary<string, PlatformApiEndpoint> Platforms { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public PlatformApiEndpoint For(string platform) =>
        Platforms.TryGetValue(platform, out var endpoint) ? endpoint : new PlatformApiEndpoint();
}

public sealed class PlatformApiEndpoint
{
    /// <summary>Raiz da API (esquema + host). Vazio = URL real da plataforma.</summary>
    public Uri? BaseUrl { get; init; }

    /// <summary>Versao da API (Shopify). Versoes sao aposentadas periodicamente: mantenha em configuracao.</summary>
    public string? ApiVersion { get; init; }
}

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Dias que o payload bruto do webhook (que contem dados pessoais do comprador) e mantido para auditoria; depois disso o
    /// conteudo e apagado e so ficam os metadados. Webhooks ainda pendentes nunca sao apagados.
    /// </summary>
    [Range(1, 3650)]
    public int RawPayloadDays { get; init; } = 30;

    /// <summary>Cron (UTC) da limpeza.</summary>
    [Required]
    public string Cron { get; init; } = "0 3 * * *";
}
