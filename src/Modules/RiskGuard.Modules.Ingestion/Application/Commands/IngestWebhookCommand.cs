using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Modules.Ingestion.Application.Parsing;
using RiskGuard.Modules.Ingestion.Application.Signatures;
using RiskGuard.Modules.Ingestion.Domain;
using RiskGuard.Modules.Ingestion.Jobs;
using RiskGuard.Modules.Ingestion.Persistence;
using StackExchange.Redis;

namespace RiskGuard.Modules.Ingestion.Application.Commands;

/// <summary>
/// RF02/RF03: recebe o webhook, autentica a origem, guarda o payload original e agenda o processamento. Nao faz trabalho
/// pesado (RNF03). <c>Body</c> sao os bytes CRUS do corpo: a assinatura so vale sobre eles.
/// </summary>
public sealed record IngestWebhookCommand(
    string Platform,
    Guid TenantId,
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string> Headers) : ICommand<Result<Guid>>;

internal sealed partial class IngestWebhookCommandHandler(
    IWebhookParserResolver parsers,
    IWebhookSignatureVerifierResolver signatureVerifiers,
    ISender sender,
    IRateLimiter rateLimiter,
    IOptions<RateLimitingOptions> rateLimitOptions,
    ITenantContextSetter tenantContext,
    IngestionDbContext db,
    IBackgroundJobScheduler scheduler,
    TimeProvider timeProvider,
    ILogger<IngestWebhookCommandHandler> logger) : IRequestHandler<IngestWebhookCommand, Result<Guid>>
{
    // Mesma resposta para tenant inexistente e inativo: nao revela quais tenants existem.
    private static readonly Error TenantNotFound =
        new("webhook.tenant_not_found", "Tenant nao encontrado.", ErrorType.NotFound);

    // Mesma resposta para "sem conexao/segredo", "sem header" e "assinatura errada".
    private static readonly Error InvalidSignature =
        new("webhook.invalid_signature", "Assinatura do webhook ausente ou invalida.", ErrorType.Unauthorized);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ILogger<IngestWebhookCommandHandler> _logger = logger;

    public async Task<Result<Guid>> Handle(IngestWebhookCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parser = parsers.Resolve(request.Platform);
        var verifier = signatureVerifiers.Resolve(request.Platform);
        if (parser is null || verifier is null)
        {
            return new Error("webhook.unsupported_platform", "Plataforma nao suportada.", ErrorType.NotFound);
        }

        // 1) tenant via contrato do TenantModule (cache Redis) -- nunca o banco do outro modulo
        var tenant = await sender.Send(new GetTenantSnapshotQuery(request.TenantId), cancellationToken).ConfigureAwait(false);
        if (tenant is not { IsActive: true })
        {
            return TenantNotFound;
        }

        // 2) rate limit por tenant (Redis), ANTES de qualquer trabalho caro: protege contra inundacao de webhooks forjados
        var limited = await CheckRateLimitAsync(request.TenantId, cancellationToken).ConfigureAwait(false);
        if (limited is not null)
        {
            return limited;
        }

        // 3) autenticidade: HMAC do corpo cru com o segredo do lojista (fail-closed se nao houver segredo)
        var credentials = await sender.Send(new GetPlatformCredentialsQuery(request.TenantId, parser.Platform), cancellationToken).ConfigureAwait(false);
        if (credentials?.WebhookSecret is not { Length: > 0 } secret || !verifier.IsValid(request.Headers, request.Body.Span, secret))
        {
            return InvalidSignature;
        }

        // 4) so aceitamos JSON bem formado (o conteudo especifico e validado depois, no job)
        if (!TryDecodeJson(request.Body, out var payload))
        {
            return new Error("webhook.invalid_json", "O corpo deve ser um JSON valido (UTF-8).", ErrorType.Validation);
        }

        // 5) persiste o raw payload (RLS exige o tenant na sessao) e agenda o processamento
        tenantContext.SetTenant(request.TenantId);
        var webhook = RawWebhook.Receive(
            request.TenantId,
            parser.Platform,
            WebhookHeaders.ToJson(request.Headers),
            payload,
            timeProvider.GetUtcNow());

        db.RawWebhooks.Add(webhook);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        scheduler.Enqueue<ProcessWebhookJob>(job => job.RunAsync(request.TenantId, webhook.Id));

        return webhook.Id;
    }

    private async Task<Error?> CheckRateLimitAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var options = rateLimitOptions.Value;
        try
        {
            var result = await rateLimiter
                .TryAcquireAsync($"webhook:{tenantId}", options.WebhookTenantPermitLimit, TimeSpan.FromSeconds(options.WindowSeconds), cancellationToken)
                .ConfigureAwait(false);

            return result.Allowed
                ? null
                : new Error("webhook.rate_limited", "Limite de requisicoes excedido.", ErrorType.RateLimited, result.RetryAfter);
        }
        catch (RedisException ex)
        {
            LogRateLimiterUnavailable(ex); // fail-open: nao perdemos webhooks por falha do limitador
            return null;
        }
    }

    private static bool TryDecodeJson(ReadOnlyMemory<byte> body, out string payload)
    {
        payload = string.Empty;
        if (body.IsEmpty)
        {
            return false;
        }

        try
        {
            payload = StrictUtf8.GetString(body.Span);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            using var _ = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64 });
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis indisponivel; rate limit do webhook ignorado (fail-open).")]
    private partial void LogRateLimiterUnavailable(Exception exception);
}
