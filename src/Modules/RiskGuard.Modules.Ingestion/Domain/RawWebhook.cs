using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Ingestion.Domain;

public enum WebhookStatus
{
    Received,
    Processed,
    Failed,
}

/// <summary>Webhook como recebido (RF03): payload e headers originais preservados para auditoria.</summary>
public sealed class RawWebhook : Entity, ITenantEntity
{
    public const int ErrorMaxLength = 2000;

    private RawWebhook()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    public string Platform { get; private set; } = string.Empty;

    /// <summary>JSON (jsonb) com os headers sem dados sensiveis.</summary>
    public string Headers { get; private set; } = "{}";

    /// <summary>JSON (jsonb) exatamente como recebido (normalizado pelo PostgreSQL).</summary>
    public string Payload { get; private set; } = "{}";

    public WebhookStatus Status { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Quando o conteudo (dados pessoais) foi apagado por retencao ou por anonimizacao; so os metadados permanecem.</summary>
    public DateTimeOffset? PayloadPurgedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static RawWebhook Receive(Guid tenantId, string platform, string headersJson, string payload, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new RawWebhook
        {
            TenantId = tenantId,
            Platform = platform,
            Headers = headersJson,
            Payload = payload,
            Status = WebhookStatus.Received,
            ReceivedAt = now,
        };
    }

    public bool IsPending => Status == WebhookStatus.Received;

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = WebhookStatus.Processed;
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>Falha permanente (payload invalido para a plataforma): nao deve ser reprocessada.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = WebhookStatus.Failed;
        ProcessedAt = now;
        LastError = Truncate(error);
    }

    /// <summary>Falha transitoria: registra a tentativa; apos esgotar as tentativas automaticas (RNF04) vira <see cref="WebhookStatus.Failed"/>.</summary>
    public void RecordFailedAttempt(string error, int maxRetries, DateTimeOffset now)
    {
        Attempts++;
        LastError = Truncate(error);

        // 1 execucao inicial + maxRetries reexecucoes
        if (Attempts > maxRetries)
        {
            Status = WebhookStatus.Failed;
            ProcessedAt = now;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= ErrorMaxLength ? value : value[..ErrorMaxLength];
}
