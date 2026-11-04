using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Audit;

/// <summary>Apaga eventos de auditoria mais antigos que o corte. A RLS ainda recusa qualquer evento com menos de 180 dias.</summary>
public sealed record PurgeExpiredAuditEventsCommand(Guid TenantId, DateTimeOffset OlderThan) : ICommand<int>;

internal sealed class PurgeExpiredAuditEventsCommandHandler(TenantDbContext db, ITenantContextSetter tenantContext)
    : IRequestHandler<PurgeExpiredAuditEventsCommand, int>
{
    public async Task<int> Handle(PurgeExpiredAuditEventsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        tenantContext.SetTenant(request.TenantId);
        return await db.AuditEvents
            .Where(e => e.OccurredAt < request.OlderThan)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Job recorrente (Hangfire): percorre os tenants, um por vez, sob RLS.</summary>
public sealed partial class PurgeExpiredAuditEventsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<PurgeExpiredAuditEventsJob> logger)
{
    private readonly ILogger<PurgeExpiredAuditEventsJob> _logger = logger;

    public async Task RunAsync()
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-options.Value.AuditDays);

        List<Guid> tenantIds;
        await using (var listScope = scopeFactory.CreateAsyncScope())
        {
            tenantIds = await listScope.ServiceProvider.GetRequiredService<TenantDbContext>().Tenants
                .AsNoTracking().Select(t => t.Id).ToListAsync().ConfigureAwait(false);
        }

        var purged = 0;
        var failures = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                purged += await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new PurgeExpiredAuditEventsCommand(tenantId, cutoff)).ConfigureAwait(false);
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
            throw new InvalidOperationException($"A limpeza da auditoria falhou para {failures} tenant(s); veja os logs.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao limpar a auditoria antiga do tenant {TenantId}.")]
    private partial void LogTenantFailed(Exception exception, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retencao: {Purged} evento(s) de auditoria apagado(s) em {Tenants} tenant(s).")]
    private partial void LogCompleted(int purged, int tenants);
}

/// <summary>Agenda a limpeza da auditoria ao subir a aplicacao (idempotente: AddOrUpdate).</summary>
internal sealed class AuditRetentionJobRegistrar(IBackgroundJobScheduler scheduler, IOptions<RetentionOptions> options) : IHostedService
{
    public const string JobId = "tenant-purge-audit-events";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            scheduler.AddRecurring<PurgeExpiredAuditEventsJob>(JobId, job => job.RunAsync(), options.Value.Cron);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
