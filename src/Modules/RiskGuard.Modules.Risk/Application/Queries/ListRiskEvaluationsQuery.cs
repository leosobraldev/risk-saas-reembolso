using MediatR;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Risk.Persistence;

namespace RiskGuard.Modules.Risk.Application.Queries;

public sealed record RiskFactorDto(string Reason, int PointsAdded);

public sealed record RiskEvaluationDto(
    Guid Id,
    Guid OrderId,
    string Platform,
    string ExternalOrderId,
    decimal TotalAmount,
    int Score,
    string Level,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<RiskFactorDto> Factors);

public sealed record RiskEvaluationPage(IReadOnlyList<RiskEvaluationDto> Items, int Page, int PageSize, int Total);

/// <summary>Dashboard (SDD 8): avaliacoes do lojista autenticado, mais recentes primeiro.</summary>
public sealed record ListRiskEvaluationsQuery(int Page = 1, int PageSize = 20, RiskLevel? Level = null)
    : IQuery<Result<RiskEvaluationPage>>
{
    public const int MaxPageSize = 100;
}

internal sealed class ListRiskEvaluationsQueryHandler(RiskDbContext db, ITenantContext tenantContext)
    : IRequestHandler<ListRiskEvaluationsQuery, Result<RiskEvaluationPage>>
{
    public async Task<Result<RiskEvaluationPage>> Handle(ListRiskEvaluationsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!tenantContext.HasTenant)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        if (request.Page < 1 || request.PageSize < 1)
        {
            return new Error("risk.invalid_paging", "page e pageSize devem ser maiores que zero.", ErrorType.Validation);
        }

        var pageSize = Math.Min(request.PageSize, ListRiskEvaluationsQuery.MaxPageSize);

        // Global Query Filter (EF) + RLS (banco) restringem ao tenant do token.
        var query = db.Evaluations.AsNoTracking();
        if (request.Level is { } level)
        {
            query = query.Where(e => e.Level == level);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(e => e.CalculatedAt)
            .ThenBy(e => e.Id)
            .Skip((request.Page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new RiskEvaluationDto(
                e.Id,
                e.OrderId,
                e.Platform,
                e.ExternalOrderId,
                e.TotalAmount,
                e.Score,
                e.Level.ToString(),
                e.CalculatedAt,
                e.Factors.Select(f => new RiskFactorDto(f.Reason, f.PointsAdded)).ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RiskEvaluationPage(items, request.Page, pageSize, total);
    }
}
