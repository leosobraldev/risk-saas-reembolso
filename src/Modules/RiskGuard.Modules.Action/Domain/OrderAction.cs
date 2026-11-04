using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Action.Domain;

public enum OrderActionType
{
    /// <summary>Pausa o pedido na plataforma (ex.: hold de fulfillment na Shopify).</summary>
    Block,

    /// <summary>A plataforma nao tem hold: o pedido e apenas sinalizado ao lojista (ex.: nota interna na Nuvemshop).</summary>
    Flag,
}

public enum OrderActionStatus
{
    /// <summary>Aguardando (ou repetindo) a chamada a API da plataforma.</summary>
    Pending,

    Succeeded,

    /// <summary>Falha permanente ou retries esgotados (RNF04).</summary>
    Failed,

    /// <summary>Risco alto reconhecido, mas o lojista nao habilitou o bloqueio automatico: so recomendacao.</summary>
    Skipped,

    /// <summary>O lojista pediu a liberacao; o job esta desfazendo o bloqueio na plataforma.</summary>
    Releasing,

    Released,

    /// <summary>A liberacao falhou (permanente ou retries esgotados); o lojista pode pedir de novo.</summary>
    ReleaseFailed,
}

public enum ReleaseRequestOutcome
{
    Started,
    AlreadyReleasing,
    NotAllowed,
}

/// <summary>Acao preventiva sobre um pedido (uma por avaliacao de risco): trilha de auditoria e controle de retry.</summary>
public sealed class OrderAction : Entity, ITenantEntity
{
    public const int ErrorMaxLength = 2000;

    private OrderAction()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    /// <summary>Chave de idempotencia: um evento de risco repetido nao gera segunda acao.</summary>
    public Guid EvaluationId { get; private set; }

    public Guid OrderId { get; private set; }

    public string Platform { get; private set; } = string.Empty;

    public string ExternalOrderId { get; private set; } = string.Empty;

    public int Score { get; private set; }

    public OrderActionType Type { get; private set; }

    public OrderActionStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsPending => Status == OrderActionStatus.Pending;

    public bool IsReleasing => Status == OrderActionStatus.Releasing;

    public static OrderAction CreatePending(
        Guid tenantId,
        Guid evaluationId,
        Guid orderId,
        string platform,
        string externalOrderId,
        int score,
        OrderActionType type,
        DateTimeOffset now) =>
        Create(tenantId, evaluationId, orderId, platform, externalOrderId, score, type, now);

    public static OrderAction CreateSkipped(
        Guid tenantId,
        Guid evaluationId,
        Guid orderId,
        string platform,
        string externalOrderId,
        int score,
        OrderActionType type,
        string reason,
        DateTimeOffset now)
    {
        var action = Create(tenantId, evaluationId, orderId, platform, externalOrderId, score, type, now);
        action.Status = OrderActionStatus.Skipped;
        action.LastError = Truncate(reason);
        action.CompletedAt = now;
        return action;
    }

    // ---- aplicar a acao ----

    public void MarkSucceeded(DateTimeOffset now)
    {
        Ensure(OrderActionStatus.Pending);
        Status = OrderActionStatus.Succeeded;
        CompletedAt = now;
        LastError = null;
    }

    /// <summary>Falha permanente: repetir nao adianta (ex.: pedido inexistente na plataforma).</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Ensure(OrderActionStatus.Pending);
        Status = OrderActionStatus.Failed;
        CompletedAt = now;
        LastError = Truncate(error);
    }

    /// <summary>Falha transitoria: continua pendente; apos esgotar 1 execucao + <paramref name="maxRetries"/> reexecucoes vira Failed.</summary>
    public void RecordFailedAttempt(string error, int maxRetries, DateTimeOffset now)
    {
        Ensure(OrderActionStatus.Pending);
        Attempts++;
        LastError = Truncate(error);

        if (Attempts > maxRetries)
        {
            Status = OrderActionStatus.Failed;
            CompletedAt = now;
        }
    }

    // ---- liberar (desfazer) a acao ----

    /// <summary>So acoes aplicadas com sucesso (ou cuja liberacao falhou) podem ser liberadas.</summary>
    public ReleaseRequestOutcome RequestRelease()
    {
        switch (Status)
        {
            case OrderActionStatus.Releasing:
                return ReleaseRequestOutcome.AlreadyReleasing;
            case OrderActionStatus.Succeeded or OrderActionStatus.ReleaseFailed:
                Status = OrderActionStatus.Releasing;
                Attempts = 0;
                LastError = null;
                CompletedAt = null;
                return ReleaseRequestOutcome.Started;
            default:
                return ReleaseRequestOutcome.NotAllowed;
        }
    }

    public void MarkReleased(DateTimeOffset now)
    {
        Ensure(OrderActionStatus.Releasing);
        Status = OrderActionStatus.Released;
        CompletedAt = now;
        LastError = null;
    }

    public void MarkReleaseFailed(string error, DateTimeOffset now)
    {
        Ensure(OrderActionStatus.Releasing);
        Status = OrderActionStatus.ReleaseFailed;
        CompletedAt = now;
        LastError = Truncate(error);
    }

    public void RecordFailedReleaseAttempt(string error, int maxRetries, DateTimeOffset now)
    {
        Ensure(OrderActionStatus.Releasing);
        Attempts++;
        LastError = Truncate(error);

        if (Attempts > maxRetries)
        {
            Status = OrderActionStatus.ReleaseFailed;
            CompletedAt = now;
        }
    }

    private static OrderAction Create(
        Guid tenantId,
        Guid evaluationId,
        Guid orderId,
        string platform,
        string externalOrderId,
        int score,
        OrderActionType type,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        if (evaluationId == Guid.Empty)
        {
            throw new ArgumentException("EvaluationId nao pode ser vazio.", nameof(evaluationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalOrderId);
        ArgumentOutOfRangeException.ThrowIfLessThan(score, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(score, 100);

        return new OrderAction
        {
            TenantId = tenantId,
            EvaluationId = evaluationId,
            OrderId = orderId,
            Platform = platform,
            ExternalOrderId = externalOrderId,
            Score = score,
            Type = type,
            Status = OrderActionStatus.Pending,
            CreatedAt = now,
        };
    }

    private void Ensure(OrderActionStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Operacao invalida: a acao esta em '{Status}', esperado '{expected}'.");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= ErrorMaxLength ? value : value[..ErrorMaxLength];
}
