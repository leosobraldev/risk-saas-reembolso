using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;
using StackExchange.Redis;

namespace RiskGuard.Infrastructure.Caching;

/// <summary>Janela fixa atomica (INCR + PEXPIRE em um unico script Lua), segura entre varias instancias da API.</summary>
internal sealed class RedisRateLimiter(IConnectionMultiplexer redis, IOptions<RedisOptions> options) : IRateLimiter
{
    private const string Script = """
        local current = redis.call('INCR', KEYS[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if current == 1 or ttl < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
            ttl = tonumber(ARGV[1])
        end
        return {current, ttl}
        """;

    private readonly string _prefix = options.Value.InstanceName + "ratelimit:";

    public async Task<RateLimitResult> TryAcquireAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var result = (RedisResult[]?)await redis.GetDatabase()
            .ScriptEvaluateAsync(
                Script,
                [(RedisKey)(_prefix + key)],
                [(RedisValue)(long)window.TotalMilliseconds])
            .ConfigureAwait(false);

        var count = (long)result![0];
        var ttlMs = (long)result[1];

        return count <= limit
            ? new RateLimitResult(true, (int)(limit - count), TimeSpan.Zero)
            : new RateLimitResult(false, 0, TimeSpan.FromMilliseconds(ttlMs));
    }
}
