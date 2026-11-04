using MediatR;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Audit;

public sealed record AuditEventDto(Guid Id, DateTimeOffset OccurredAt, string ActorId, string Action, string? Target, string? Details);

public sealed record AuditEventPage(IReadOnlyList<AuditEventDto> Items, int Page, int PageSize, int Total);

/// <summary>GET /api/v1/audit-events: trilha do lojista autenticado, do mais recente ao mais antigo.</summary>
public sealed record ListAuditEventsQuery(int Page = 1, int PageSize = 20, string? Action = null) : IQuery<Result<AuditEventPage>>
{
    public const int MaxPageSize = 100;
}

internal sealed class ListAuditEventsQueryHandler(TenantDbContext db, ITenantContext tenantContext)
    : IRequestHandler<ListAuditEventsQuery, Result<AuditEventPage>>
{
    public async Task<Result<AuditEventPage>> Handle(ListAuditEventsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!tenantContext.HasTenant)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        if (request.Page < 1 || request.PageSize < 1)
        {
            return new Error("audit.invalid_paging", "page e pageSize devem ser maiores que zero.", ErrorType.Validation);
        }

        var pageSize = Math.Min(request.PageSize, ListAuditEventsQuery.MaxPageSize);

        // Global Query Filter (EF) + RLS (banco) restringem ao tenant do token.
        var query = db.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(e => e.Action == action);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Skip((request.Page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditEventDto(e.Id, e.OccurredAt, e.ActorId, e.Action, e.Target, e.Details))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuditEventPage(items, request.Page, pageSize, total);
    }
}
