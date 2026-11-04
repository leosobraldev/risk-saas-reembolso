namespace RiskGuard.Core.Abstractions;

/// <summary>Resultado de uma tentativa de consumo de quota.</summary>
public readonly record struct RateLimitResult(bool Allowed, int Remaining, TimeSpan RetryAfter);

/// <summary>Rate limiter distribuido (janela fixa) para protecao contra picos.</summary>
public interface IRateLimiter
{
    Task<RateLimitResult> TryAcquireAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
