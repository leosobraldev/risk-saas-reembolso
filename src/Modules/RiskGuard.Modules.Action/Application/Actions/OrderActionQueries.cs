using MediatR;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Action.Domain;
using RiskGuard.Modules.Action.Jobs;
using RiskGuard.Modules.Action.Persistence;

namespace RiskGuard.Modules.Action.Application.Actions;

public sealed record OrderActionDto(
    Guid Id,
    Guid EvaluationId,
    Guid OrderId,
    string Platform,
    string ExternalOrderId,
    int Score,
    string Type,
    string Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    internal static OrderActionDto From(OrderAction a) => new(
        a.Id, a.EvaluationId, a.OrderId, a.Platform, a.ExternalOrderId, a.Score, a.Type.ToString(), a.Status.ToString(), a.Attempts, a.LastError, a.CreatedAt, a.CompletedAt);
}

public sealed record OrderActionPage(IReadOnlyList<OrderActionDto> Items, int Page, int PageSize, int Total);

/// <summary>GET /api/v1/actions: acoes preventivas (bloqueios, sinalizacoes, recomendacoes e falhas) do lojista autenticado.</summary>
public sealed record ListOrderActionsQuery(int Page = 1, int PageSize = 20, OrderActionStatus? Status = null) : IQuery<Result<OrderActionPage>>
{
    public const int MaxPageSize = 100;
}

internal sealed class ListOrderActionsQueryHandler(ActionDbContext db, ITenantContext tenantContext)
    : IRequestHandler<ListOrderActionsQuery, Result<OrderActionPage>>
{
    public async Task<Result<OrderActionPage>> Handle(ListOrderActionsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!tenantContext.HasTenant)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        if (request.Page < 1 || request.PageSize < 1)
        {
            return new Error("actions.invalid_paging", "page e pageSize devem ser maiores que zero.", ErrorType.Validation);
        }

        var pageSize = Math.Min(request.PageSize, ListOrderActionsQuery.MaxPageSize);

        // Global Query Filter (EF) + RLS (banco) restringem ao tenant do token.
        var query = db.OrderActions.AsNoTracking();
        if (request.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Skip((request.Page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OrderActionPage(rows.Select(OrderActionDto.From).ToList(), request.Page, pageSize, total);
    }
}

/// <summary>
/// POST /api/v1/actions/{id}/release: o lojista libera um pedido bloqueado/sinalizado (falso positivo). O desfazer na
/// plataforma e assincrono (job com retry); a resposta ja traz o estado <c>Releasing</c>.
/// </summary>
public sealed record RequestOrderActionReleaseCommand(Guid ActionId) : ICommand<Result<OrderActionDto>>;

internal sealed class RequestOrderActionReleaseCommandHandler(
    ActionDbContext db,
    ITenantContext tenantContext,
    ISender sender,
    IBackgroundJobScheduler scheduler) : IRequestHandler<RequestOrderActionReleaseCommand, Result<OrderActionDto>>
{
    public async Task<Result<OrderActionDto>> Handle(RequestOrderActionReleaseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is not { } tenantId)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        // Outro tenant nao enxerga a acao (filtro + RLS): mesma resposta de "inexistente".
        var action = await db.OrderActions.FirstOrDefaultAsync(a => a.Id == request.ActionId, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return new Error("action.not_found", "Acao nao encontrada.", ErrorType.NotFound);
        }

        var outcome = action.RequestRelease();
        switch (outcome)
        {
            case ReleaseRequestOutcome.NotAllowed:
                return new Error(
                    "action.not_releasable",
                    $"Uma acao em '{action.Status}' nao pode ser liberada (so acoes aplicadas com sucesso).",
                    ErrorType.Conflict);

            case ReleaseRequestOutcome.AlreadyReleasing:
                return OrderActionDto.From(action); // pedido repetido: nao enfileira outro job

            default:
                // Registra ANTES de efetivar: se a auditoria falhar, nada foi liberado nem enfileirado.
                var audited = await sender.Send(
                    new RecordAuditEventCommand(tenantId, AuditActions.ActionReleaseRequested, action.Id.ToString(), $"{action.Platform}; pedido {action.ExternalOrderId}"),
                    cancellationToken).ConfigureAwait(false);
                if (!audited.IsSuccess)
                {
                    return audited.Error;
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var actionId = action.Id;
                scheduler.Enqueue<ExecuteOrderActionJob>(job => job.RunAsync(tenantId, actionId));
                return OrderActionDto.From(action);
        }
    }
}
