namespace RiskGuard.Core.Abstractions;

/// <summary>Cache distribuido (Redis) para configuracoes de tenant e dados quentes.</summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
