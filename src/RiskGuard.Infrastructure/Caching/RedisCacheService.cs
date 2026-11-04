using System.Text.Json;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;
using StackExchange.Redis;

namespace RiskGuard.Infrastructure.Caching;

internal sealed class RedisCacheService(IConnectionMultiplexer redis, IOptions<RedisOptions> options) : ICacheService
{
    private readonly string _prefix = options.Value.InstanceName;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await redis.GetDatabase().StringGetAsync(Prefixed(key)).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>((string)value!);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL deve ser positivo.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetDatabase()
            .StringSetAsync(Prefixed(key), JsonSerializer.Serialize(value), ttl)
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetDatabase().KeyDeleteAsync(Prefixed(key)).ConfigureAwait(false);
    }

    private RedisKey Prefixed(string key) => _prefix + key;
}
