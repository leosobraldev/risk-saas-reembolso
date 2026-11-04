namespace RiskGuard.Api.Middleware;

/// <summary>
/// Headers de defesa em profundidade para uma API JSON: sem sniffing de tipo, sem framing, sem referrer, sem cache de respostas
/// autenticadas e CSP que proibe qualquer conteudo ativo. O painel do Hangfire (so Development) usa scripts inline e fica de fora.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string HangfirePrefix = "/hangfire";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(HangfirePrefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                var headers = response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                headers["Cache-Control"] = "no-store"; // dados de lojista e de compradores nunca em cache compartilhado
                return Task.CompletedTask;
            }, context.Response);
        }

        return next(context);
    }
}
