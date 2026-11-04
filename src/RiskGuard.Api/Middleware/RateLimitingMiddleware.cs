using System.Globalization;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;
using StackExchange.Redis;

namespace RiskGuard.Api.Middleware;

/// <summary>
/// Protecao contra picos (RNF05) por IP de origem nas rotas <c>/api</c>. Se o Redis estiver indisponivel
/// a requisicao e liberada (fail-open): ingestao de webhooks nao pode cair por causa do limitador.
/// </summary>
internal sealed partial class RateLimitingMiddleware(
    RequestDelegate next,
    IRateLimiter rateLimiter,
    IOptions<RateLimitingOptions> options,
    ILogger<RateLimitingMiddleware> logger)
{
    private const string WebhookPrefix = "/api/v1/webhooks";

    private readonly RateLimitingOptions _options = options.Value;
    private readonly ILogger<RateLimitingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Plataformas de e-commerce enviam muito trafego de poucos IPs: webhooks tem teto proprio por IP
        // (o limite por tenant e aplicado pelo IngestionModule).
        var isWebhook = context.Request.Path.StartsWithSegments(WebhookPrefix, StringComparison.OrdinalIgnoreCase);
        var limit = isWebhook ? _options.WebhookIpPermitLimit : _options.PermitLimit;
        var key = (isWebhook ? "webhook-ip:" : "ip:") + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        RateLimitResult result;
        try
        {
            result = await rateLimiter.TryAcquireAsync(
                key,
                limit,
                TimeSpan.FromSeconds(_options.WindowSeconds),
                context.RequestAborted);
        }
        catch (RedisException ex)
        {
            LogRateLimiterUnavailable(ex);
            await next(context);
            return;
        }

        if (!result.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter =
                Math.Ceiling(result.RetryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return;
        }

        await next(context);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis indisponivel; rate limiting ignorado (fail-open).")]
    private partial void LogRateLimiterUnavailable(Exception exception);
}
