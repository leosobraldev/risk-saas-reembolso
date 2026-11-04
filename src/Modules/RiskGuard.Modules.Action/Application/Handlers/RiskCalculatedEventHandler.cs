using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Action.Application.Blockers;
using RiskGuard.Modules.Action.Domain;
using RiskGuard.Modules.Action.Jobs;
using RiskGuard.Modules.Action.Persistence;

namespace RiskGuard.Modules.Action.Application.Handlers;

/// <summary>
/// Criterio de aceite 3: ao receber <see cref="RiskCalculatedEvent"/>, le a configuracao do lojista (cache Redis via
/// contrato do TenantModule) e decide. Nao chama a plataforma aqui: registra a acao e agenda um job proprio, de modo que
/// uma API externa lenta ou fora do ar nunca atrasa nem falha a ingestao do webhook.
/// </summary>
internal sealed class RiskCalculatedEventHandler(
    ActionDbContext db,
    ISender sender,
    IOrderBlockerResolver blockers,
    ITenantContextSetter tenantContext,
    IBackgroundJobScheduler scheduler,
    TimeProvider timeProvider) : INotificationHandler<RiskCalculatedEvent>
{
    public async Task Handle(RiskCalculatedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        tenantContext.SetTenant(notification.TenantId);

        var tenant = await sender.Send(new GetTenantSnapshotQuery(notification.TenantId), cancellationToken).ConfigureAwait(false);
        var decision = ActionPolicy.Decide(notification.Score, tenant);
        if (decision == ActionDecision.None)
        {
            return;
        }

        var existing = await db.OrderActions
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.EvaluationId == notification.EvaluationId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Evento repetido. Se a acao ainda esta pendente, o agendamento anterior pode ter se perdido: reagenda
            // (o job e idempotente e a plataforma recebe sempre a mesma Idempotency-Key).
            if (existing.IsPending)
            {
                Schedule(existing);
            }

            return;
        }

        // O que a plataforma permite (pausar ou so sinalizar) define o tipo da acao.
        var type = blockers.Resolve(notification.Platform)?.ActionType ?? OrderActionType.Block;
        var now = timeProvider.GetUtcNow();
        var action = decision == ActionDecision.Block
            ? OrderAction.CreatePending(notification.TenantId, notification.EvaluationId, notification.OrderId, notification.Platform, notification.ExternalOrderId, notification.Score, type, now)
            : OrderAction.CreateSkipped(
                notification.TenantId,
                notification.EvaluationId,
                notification.OrderId,
                notification.Platform,
                notification.ExternalOrderId,
                notification.Score,
                type,
                "Risco alto, mas o bloqueio automatico (auto-block) esta desligado para este lojista.",
                now);

        db.OrderActions.Add(action);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return; // outro processamento do mesmo evento ja criou a acao
        }

        if (action.IsPending)
        {
            Schedule(action);
        }
    }

    private void Schedule(OrderAction action)
    {
        var tenantId = action.TenantId;
        var actionId = action.Id;
        scheduler.Enqueue<ExecuteOrderActionJob>(job => job.RunAsync(tenantId, actionId));
    }
}
