using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Tenant.Application.Audit;
using RiskGuard.Modules.Tenant.Application.Queries;
using RiskGuard.Modules.Tenant.Persistence;
using StackExchange.Redis;

namespace RiskGuard.Modules.Tenant.Application.Settings;

/// <summary>Remove o snapshot do tenant do Redis para que Ingestion/Risk/Action vejam a mudanca na hora (e nao em ate 5 min).</summary>
internal sealed partial class TenantSnapshotInvalidator(ICacheService cache, ILogger<TenantSnapshotInvalidator> logger)
{
    private readonly ILogger<TenantSnapshotInvalidator> _logger = logger;

    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(GetTenantSnapshotQueryHandler.CacheKey(tenantId), cancellationToken).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // Sem Redis nao ha cache para invalidar; o TTL de 5 min limita a defasagem se ele voltar com o valor antigo.
            LogInvalidationFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis indisponivel ao invalidar o snapshot do tenant.")]
    private partial void LogInvalidationFailed(Exception exception);
}

/// <summary>PUT /api/v1/tenants/me/settings: limite de risco alto e bloqueio automatico do lojista autenticado.</summary>
public sealed record UpdateTenantSettingsCommand(int HighRiskThreshold, bool AutoBlockEnabled) : ICommand<Result<TenantSettingsDto>>;

internal sealed class UpdateTenantSettingsCommandHandler(
    TenantDbContext db,
    ITenantContext tenantContext,
    IActorContext actor,
    TimeProvider timeProvider,
    TenantSnapshotInvalidator invalidator) : IRequestHandler<UpdateTenantSettingsCommand, Result<TenantSettingsDto>>
{
    public async Task<Result<TenantSettingsDto>> Handle(UpdateTenantSettingsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is not { } tenantId)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        var settings = await db.Settings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            return new Error("tenant.settings_not_found", "Configuracoes do lojista nao encontradas.", ErrorType.NotFound);
        }

        var (oldThreshold, oldAutoBlock) = (settings.HighRiskThreshold, settings.AutoBlockEnabled);
        var updated = settings.Update(request.HighRiskThreshold, request.AutoBlockEnabled);
        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        // Mesma transacao da mudanca: nao existe alteracao de configuracao sem rastro.
        AuditTrail.Record(
            db, actor, timeProvider, tenantId, AuditActions.SettingsUpdated, "settings",
            $"limite {oldThreshold} -> {settings.HighRiskThreshold}; bloqueio automatico {(oldAutoBlock ? "ligado" : "desligado")} -> {(settings.AutoBlockEnabled ? "ligado" : "desligado")}");
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Depois do commit: quem recarregar o cache a partir daqui le o valor novo.
        await invalidator.InvalidateAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return new TenantSettingsDto(settings.HighRiskThreshold, settings.AutoBlockEnabled);
    }
}

/// <summary>Desativa o lojista (operacao administrativa): para de aceitar webhooks e de agir em seus pedidos imediatamente.</summary>
public sealed record DeactivateTenantCommand(Guid TenantId) : ICommand<Result>;

internal sealed partial class DeactivateTenantCommandHandler(
    TenantDbContext db,
    TenantSnapshotInvalidator invalidator,
    IIdentityProvider identityProvider,
    ITenantContextSetter tenantContext,
    IActorContext actor,
    TimeProvider timeProvider,
    ILogger<DeactivateTenantCommandHandler> logger) : IRequestHandler<DeactivateTenantCommand, Result>
{
    private readonly ILogger<DeactivateTenantCommandHandler> _logger = logger;

    public async Task<Result> Handle(DeactivateTenantCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // O tenant do escopo precisa estar definido ANTES da primeira conexao (a RLS da auditoria e dos usuarios o exige).
        tenantContext.SetTenant(request.TenantId);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Result.Failure(new Error("tenant.not_found", "Lojista nao encontrado.", ErrorType.NotFound));
        }

        tenant.Deactivate();
        AuditTrail.Record(db, actor, timeProvider, tenant.Id, AuditActions.TenantDeactivated, "tenant");
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Efeito imediato na API: o snapshot sai do cache e o middleware de tenant ativo passa a recusar os tokens ja emitidos.
        await invalidator.InvalidateAsync(tenant.Id, cancellationToken).ConfigureAwait(false);

        // Alem disso, o provedor passa a negar novos logins e renovacoes. Melhor esforco: a API ja esta protegida pelo passo acima.
        var userIds = await db.TenantUsers.AsNoTracking().Select(u => u.ExternalUserId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds)
        {
            var blocked = await identityProvider.SetBlockedAsync(userId, blocked: true, cancellationToken).ConfigureAwait(false);
            if (!blocked.IsSuccess)
            {
                LogBlockFailed(tenant.Id, blocked.Error.Code);
            }
        }

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lojista {TenantId} desativado, mas o bloqueio no provedor de identidade falhou ({ErrorCode}). A API ja recusa os tokens; bloqueie o usuario no provedor.")]
    private partial void LogBlockFailed(Guid tenantId, string errorCode);
}
