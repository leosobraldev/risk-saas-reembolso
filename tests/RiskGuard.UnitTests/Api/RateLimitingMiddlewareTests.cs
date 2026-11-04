using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RiskGuard.Api.Middleware;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;
using StackExchange.Redis;

namespace RiskGuard.UnitTests.Api;

public class RateLimitingMiddlewareTests
{
    private readonly IRateLimiter _limiter = Substitute.For<IRateLimiter>();

    [Fact]
    public async Task Allowed_request_continues_the_pipeline()
    {
        _limiter.TryAcquireAsync(default!, default, default, default)
            .ReturnsForAnyArgs(new RateLimitResult(true, 9, TimeSpan.Zero));

        var (context, nextCalled) = await Run("/api/v1/webhooks/shopify/x");

        nextCalled().Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Exceeded_quota_returns_429_with_Retry_After_and_short_circuits()
    {
        _limiter.TryAcquireAsync(default!, default, default, default)
            .ReturnsForAnyArgs(new RateLimitResult(false, 0, TimeSpan.FromSeconds(12.2)));

        var (context, nextCalled) = await Run("/api/v1/webhooks/shopify/x");

        nextCalled().Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.Headers.RetryAfter.ToString().Should().Be("13");
    }

    [Fact]
    public async Task Non_api_routes_are_not_limited()
    {
        var (_, nextCalled) = await Run("/health/ready");

        nextCalled().Should().BeTrue();
        await _limiter.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Redis_failure_fails_open()
    {
        _limiter.TryAcquireAsync(default!, default, default, default)
            .ReturnsForAnyArgs(Task.FromException<RateLimitResult>(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")));

        var (context, nextCalled) = await Run("/api/v1/risk/evaluations");

        nextCalled().Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private async Task<(DefaultHttpContext Context, Func<bool> NextCalled)> Run(string path)
    {
        var called = false;
        var middleware = new RateLimitingMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            _limiter,
            Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions { PermitLimit = 10, WindowSeconds = 60 }),
            NullLogger<RateLimitingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);
        return (context, () => called);
    }
}
