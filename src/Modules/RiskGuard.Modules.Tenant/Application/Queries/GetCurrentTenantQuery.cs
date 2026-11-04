using MediatR;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Queries;

public sealed record TenantSettingsDto(int HighRiskThreshold, bool AutoBlockEnabled);

public sealed record CurrentTenantDto(Guid Id, string Name, string Email, TenantSettingsDto Settings);

/// <summary>Dados do lojista autenticado (tenant vem do contexto, nunca de parametro do cliente).</summary>
public sealed record GetCurrentTenantQuery : IQuery<Result<CurrentTenantDto>>;

internal sealed class GetCurrentTenantQueryHandler(TenantDbContext db, ITenantContext tenantContext)
    : IRequestHandler<GetCurrentTenantQuery, Result<CurrentTenantDto>>
{
    public async Task<Result<CurrentTenantDto>> Handle(GetCurrentTenantQuery request, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return new Error("tenant.not_found", "Lojista nao encontrado.", ErrorType.NotFound);
        }

        if (!tenant.IsActive)
        {
            return new Error("tenant.inactive", "Lojista inativo.", ErrorType.Forbidden);
        }

        // Filtrado por Global Query Filter (EF) e por RLS (banco).
        var settings = await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            return new Error("tenant.settings_not_found", "Configuracoes do lojista nao encontradas.", ErrorType.NotFound);
        }

        return new CurrentTenantDto(
            tenant.Id,
            tenant.Name,
            tenant.Email,
            new TenantSettingsDto(settings.HighRiskThreshold, settings.AutoBlockEnabled));
    }
}
