using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Abstractions;
using RiskGuard.IntegrationTests.Infrastructure;

namespace RiskGuard.IntegrationTests.Foundation;

[Collection(InfrastructureCollection.Name)]
public class RedisTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Cache_roundtrips_values_and_honours_removal_and_ttl()
    {
        await using var factory = new ApiFactory(infrastructure);
        using var _ = factory.CreateClient();
        var cache = factory.Services.GetRequiredService<ICacheService>();
        var key = $"tenant:{Guid.NewGuid()}";

        await cache.SetAsync(key, new Settings(80, true), TimeSpan.FromMinutes(5));
        (await cache.GetAsync<Settings>(key)).Should().Be(new Settings(80, true));

        await cache.RemoveAsync(key);
        (await cache.GetAsync<Settings>(key)).Should().BeNull();

        await cache.SetAsync(key, new Settings(1, false), TimeSpan.FromMilliseconds(200));
        await Task.Delay(500);
        (await cache.GetAsync<Settings>(key)).Should().BeNull();
    }

    [Fact]
    public async Task Rate_limiter_blocks_after_the_limit_and_recovers_in_the_next_window()
    {
        await using var factory = new ApiFactory(infrastructure);
        using var _ = factory.CreateClient();
        var limiter = factory.Services.GetRequiredService<IRateLimiter>();
        var key = Guid.NewGuid().ToString();
        var window = TimeSpan.FromSeconds(1);

        var first = await limiter.TryAcquireAsync(key, 2, window);
        var second = await limiter.TryAcquireAsync(key, 2, window);
        var third = await limiter.TryAcquireAsync(key, 2, window);

        first.Should().Be(new RateLimitResult(true, 1, TimeSpan.Zero));
        second.Allowed.Should().BeTrue();
        third.Allowed.Should().BeFalse();
        third.RetryAfter.Should().BePositive();

        await Task.Delay(window + TimeSpan.FromMilliseconds(300));
        (await limiter.TryAcquireAsync(key, 2, window)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Api_returns_429_once_the_ip_quota_is_exhausted()
    {
        await using var factory = new ApiFactory(infrastructure, permitLimit: 2);
        using var client = factory.CreateClient();
        var route = new Uri("/api/v1/anything", UriKind.Relative);

        // A rota nao existe (404), mas o limitador roda antes do roteamento.
        (await client.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var limited = await client.GetAsync(route);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
    }

    private sealed record Settings(int HighRiskThreshold, bool AutoBlockEnabled);
}
