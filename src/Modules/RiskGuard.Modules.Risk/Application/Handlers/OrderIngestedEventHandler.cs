using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Risk.Application.Rules;
using RiskGuard.Modules.Risk.Domain;
using RiskGuard.Modules.Risk.Persistence;

namespace RiskGuard.Modules.Risk.Application.Handlers;

/// <summary>
/// RF04: reage ao pedido normalizado (evento do IngestionModule), roda o pipeline de regras, persiste a avaliacao
/// e publica <see cref="RiskCalculatedEvent"/>. Idempotente por pedido: reentregas republicam o resultado ja calculado.
/// </summary>
internal sealed class OrderIngestedEventHandler(
    RiskDbContext db,
    RiskPipeline pipeline,
    ISender sender,
    IPublisher publisher,
    ITenantContextSetter tenantContext,
    TimeProvider timeProvider) : INotificationHandler<OrderIngestedEvent>
{
    private const int DefaultHighRiskThreshold = 80;

    public async Task Handle(OrderIngestedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        tenantContext.SetTenant(notification.TenantId);

        var evaluation = await FindAsync(notification.OrderId, cancellationToken).ConfigureAwait(false)
            ?? await EvaluateAsync(notification, cancellationToken).ConfigureAwait(false);

        await publisher.Publish(
            new RiskCalculatedEvent(
                evaluation.TenantId,
                evaluation.Id,
                evaluation.OrderId,
                evaluation.Platform,
                evaluation.ExternalOrderId,
                evaluation.Score,
                evaluation.Level),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RiskEvaluation> EvaluateAsync(OrderIngestedEvent order, CancellationToken cancellationToken)
    {
        // Limite do lojista via contrato do TenantModule (cache Redis); sem tenant conhecido, usa o padrao.
        var tenant = await sender.Send(new GetTenantSnapshotQuery(order.TenantId), cancellationToken).ConfigureAwait(false);

        var refunds = await db.CustomerHistories
            .Where(h => h.CustomerEmail == order.CustomerEmail)
            .Select(h => h.TotalRefunds)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var assessment = pipeline.Evaluate(
            new RiskContext(
                order.TenantId,
                order.OrderId,
                order.Platform,
                order.ExternalOrderId,
                order.CustomerEmail,
                order.TotalAmount,
                order.OrderCreatedAt,
                refunds),
            tenant?.HighRiskThreshold ?? DefaultHighRiskThreshold);

        var evaluation = RiskEvaluation.Create(
            order.TenantId,
            order.OrderId,
            order.Platform,
            order.ExternalOrderId,
            order.TotalAmount,
            assessment,
            timeProvider.GetUtcNow());

        db.Evaluations.Add(evaluation);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return evaluation;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Avaliacao concorrente do mesmo pedido: a outra execucao venceu.
            db.ChangeTracker.Clear();
            return await FindAsync(order.OrderId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Avaliacao duplicada nao encontrada apos violacao de unicidade.");
        }
    }

    private Task<RiskEvaluation?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Evaluations.AsNoTracking().FirstOrDefaultAsync(e => e.OrderId == orderId, cancellationToken);
}
