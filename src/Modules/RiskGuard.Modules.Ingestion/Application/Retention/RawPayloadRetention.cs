using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Modules.Ingestion.Domain;
using RiskGuard.Modules.Ingestion.Persistence;

namespace RiskGuard.Modules.Ingestion.Application.Retention;

/// <summary>Apaga o conteudo (dados pessoais) dos webhooks ja concluidos e mais antigos que o corte; os metadados permanecem.</summary>
public sealed record PurgeExpiredRawPayloadsCommand(Guid TenantId, DateTimeOffset OlderThan) : ICommand<int>;

internal sealed class PurgeExpiredRawPayloadsCommandHandler(IngestionDbContext db, ITenantContextSetter tenantContext, TimeProvider timeProvider)
    : IRequestHandler<PurgeExpiredRawPayloadsCommand, int>
{
    public async Task<int> Handle(PurgeExpiredRawPayloadsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Manutencao roda fora de requisicao HTTP: o tenant vem do comando (e a RLS impede tocar em outro).
        tenantContext.SetTenant(request.TenantId);
        var now = timeProvider.GetUtcNow();

        // Pendentes (Received) nunca sao apagados: ainda serao processados.
        return await db.RawWebhooks
            .Where(w => w.PayloadPurgedAt == null && w.Status != WebhookStatus.Received && w.ReceivedAt < request.OlderThan)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.Payload, "{}").SetProperty(w => w.Headers, "{}").SetProperty(w => w.PayloadPurgedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Job recorrente (Hangfire): percorre os tenants, um por vez, sob RLS.</summary>
public sealed partial class PurgeExpiredRawPayloadsJob(
    ISender sender,
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<PurgeExpiredRawPayloadsJob> logger)
{
    private readonly ILogger<PurgeExpiredRawPayloadsJob> _logger = logger;

    public async Task RunAsync()
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-options.Value.RawPayloadDays);
        var tenantIds = await sender.Send(new ListTenantIdsQuery()).ConfigureAwait(false);

        var purged = 0;
        var failures = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                purged += await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new PurgeExpiredRawPayloadsCommand(tenantId, cutoff)).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // um tenant com problema nao pode impedir a limpeza dos demais; o job falha ao final para o retry
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures++;
                LogTenantFailed(ex, tenantId);
            }
        }

        LogCompleted(purged, tenantIds.Count);
        if (failures > 0)
        {
            throw new InvalidOperationException($"A limpeza falhou para {failures} tenant(s); veja os logs.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao limpar payloads antigos do tenant {TenantId}.")]
    private partial void LogTenantFailed(Exception exception, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retencao: {Purged} payload(s) apagado(s) em {Tenants} tenant(s).")]
    private partial void LogCompleted(int purged, int tenants);
}

/// <summary>Agenda a limpeza diaria ao subir a aplicacao (idempotente: AddOrUpdate).</summary>
internal sealed class RetentionJobRegistrar(IBackgroundJobScheduler scheduler, IOptions<RetentionOptions> options) : IHostedService
{
    public const string JobId = "ingestion-purge-raw-payloads";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            scheduler.AddRecurring<PurgeExpiredRawPayloadsJob>(JobId, job => job.RunAsync(), options.Value.Cron);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
