using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Modules.Ingestion.Application.Parsing;
using RiskGuard.Modules.Ingestion.Domain;
using RiskGuard.Modules.Ingestion.Persistence;

namespace RiskGuard.Modules.Ingestion.Application.Commands;

/// <summary>Executado pelo Hangfire: normaliza o payload e persiste o pedido (idempotente).</summary>
public sealed record ProcessRawWebhookCommand(Guid TenantId, Guid WebhookId) : ICommand<Result>;

internal sealed class ProcessRawWebhookCommandHandler(
    IngestionDbContext db,
    IWebhookParserResolver parsers,
    IEnumerable<IPlatformOrderFetcher> fetchers,
    ISender sender,
    ITenantContextSetter tenantContext,
    IPublisher publisher,
    TimeProvider timeProvider) : IRequestHandler<ProcessRawWebhookCommand, Result>
{
    public async Task<Result> Handle(ProcessRawWebhookCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // O job roda fora de uma requisicao HTTP: o tenant vem dos argumentos do job.
        tenantContext.SetTenant(request.TenantId);

        var webhook = await db.RawWebhooks.FirstOrDefaultAsync(w => w.Id == request.WebhookId, cancellationToken).ConfigureAwait(false);
        if (webhook is null)
        {
            return Result.Failure(new Error("webhook.not_found", "Webhook nao encontrado.", ErrorType.NotFound));
        }

        if (!webhook.IsPending)
        {
            return Result.Success(); // reexecucao de um job ja concluido
        }

        var parser = parsers.Resolve(webhook.Platform);
        var parsed = parser?.Parse(webhook.Payload)
            ?? Result<NormalizedOrder>.Failure(new Error("webhook.unsupported_platform", "Plataforma nao suportada.", ErrorType.Validation));

        // Webhook so com o id (ex.: Nuvemshop): busca o pedido na API da loja com o token do lojista e processa como os demais.
        if (parser is not null && !parsed.IsSuccess && parsed.Error.Code == WebhookErrorCodes.NeedsOrderFetch)
        {
            parsed = await FetchAndParseAsync(request.TenantId, webhook, parser, cancellationToken).ConfigureAwait(false);
        }

        if (!parsed.IsSuccess)
        {
            // Falha permanente: reprocessar nao muda o resultado. Mantemos o raw para auditoria e nao relancamos.
            webhook.MarkFailed(parsed.Error.Message, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }

        var normalized = parsed.Value;
        var order = await FindOrCreateOrderAsync(request.TenantId, normalized, cancellationToken).ConfigureAwait(false);

        // Entrega at-least-once: se a publicacao falhar, o job e repetido e o webhook ainda esta pendente.
        await publisher.Publish(
            new OrderIngestedEvent(
                request.TenantId,
                order.Id,
                order.Platform,
                order.ExternalOrderId,
                order.CustomerEmail,
                order.TotalAmount,
                order.CreatedAt),
            cancellationToken).ConfigureAwait(false);

        webhook.MarkProcessed(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private async Task<Result<NormalizedOrder>> FetchAndParseAsync(
        Guid tenantId,
        RawWebhook webhook,
        IWebhookParser parser,
        CancellationToken cancellationToken)
    {
        var fetcher = fetchers.FirstOrDefault(f => f.Platform.Equals(webhook.Platform, StringComparison.OrdinalIgnoreCase));
        var externalId = ReadId(webhook.Payload);
        if (fetcher is null || externalId is null)
        {
            return new Error("webhook.order_fetch_failed", "Nao ha como buscar o pedido desta plataforma.", ErrorType.Validation);
        }

        var credentials = await sender.Send(new GetPlatformCredentialsQuery(tenantId, webhook.Platform), cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            return new Error("webhook.order_fetch_failed", "O lojista nao conectou a plataforma: pedido nao pode ser buscado.", ErrorType.Validation);
        }

        var fetched = await fetcher.FetchOrderAsync(credentials, externalId, cancellationToken).ConfigureAwait(false);
        return fetched.Status switch
        {
            ExternalApiStatus.Succeeded => parser.Parse(fetched.Body ?? string.Empty),
            ExternalApiStatus.TransientFailure => throw new TransientExternalApiException(fetched.Error ?? "Falha transitoria ao buscar o pedido."),
            _ => new Error("webhook.order_fetch_failed", fetched.Error ?? "Falha ao buscar o pedido.", ErrorType.Validation),
        };
    }

    private static string? ReadId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("id", out var id)
                ? id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Order> FindOrCreateOrderAsync(Guid tenantId, NormalizedOrder normalized, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing; // reentrega da plataforma ou retry do job
        }

        var order = Order.Create(
            tenantId,
            normalized.Platform,
            normalized.ExternalOrderId,
            normalized.CustomerEmail,
            normalized.TotalAmount,
            normalized.CreatedAt);

        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return order;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Duas entregas do mesmo pedido processadas em paralelo: a outra venceu.
            db.Entry(order).State = EntityState.Detached;
            return await FindAsync(normalized, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Pedido duplicado nao encontrado apos violacao de unicidade.");
        }
    }

    private Task<Order?> FindAsync(NormalizedOrder normalized, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(
            o => o.Platform == normalized.Platform && o.ExternalOrderId == normalized.ExternalOrderId,
            cancellationToken);
}

/// <summary>Registra uma tentativa que falhou por motivo transitorio (RNF04).</summary>
public sealed record RecordWebhookFailureCommand(Guid TenantId, Guid WebhookId, string Error) : ICommand<Result>;

internal sealed class RecordWebhookFailureCommandHandler(
    IngestionDbContext db,
    ITenantContextSetter tenantContext,
    IOptions<HangfireOptions> hangfireOptions,
    TimeProvider timeProvider) : IRequestHandler<RecordWebhookFailureCommand, Result>
{
    public async Task<Result> Handle(RecordWebhookFailureCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        tenantContext.SetTenant(request.TenantId);

        var webhook = await db.RawWebhooks.FirstOrDefaultAsync(w => w.Id == request.WebhookId, cancellationToken).ConfigureAwait(false);
        if (webhook is null || !webhook.IsPending)
        {
            return Result.Success();
        }

        webhook.RecordFailedAttempt(request.Error, hangfireOptions.Value.MaxRetryAttempts, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
