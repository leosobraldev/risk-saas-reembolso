using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Tenant.Persistence;
using StackExchange.Redis;

namespace RiskGuard.Modules.Tenant.Application.Queries;

/// <summary>Formato armazenado no Redis. <c>Snapshot == null</c> e o cache negativo (tenant inexistente).</summary>
public sealed record TenantSnapshotCacheEntry(TenantSnapshot? Snapshot);

/// <summary>
/// Responde ao contrato <see cref="GetTenantSnapshotQuery"/> com cache-aside no Redis. A leitura no banco acontece
/// em um escopo proprio com o tenant consultado, de modo que o chamador (webhook anonimo) nao herda tenant algum.
/// </summary>
internal sealed partial class GetTenantSnapshotQueryHandler(
    IServiceScopeFactory scopeFactory,
    ICacheService cache,
    ILogger<GetTenantSnapshotQueryHandler> logger) : IRequestHandler<GetTenantSnapshotQuery, TenantSnapshot?>
{
    public static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(1);

    private readonly ILogger<GetTenantSnapshotQueryHandler> _logger = logger;

    public static string CacheKey(Guid tenantId) => $"tenant:{tenantId}:snapshot";

    public async Task<TenantSnapshot?> Handle(GetTenantSnapshotQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = CacheKey(request.TenantId);

        try
        {
            var cached = await cache.GetAsync<TenantSnapshotCacheEntry>(key, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return cached.Snapshot;
            }
        }
        catch (RedisException ex)
        {
            LogCacheUnavailable(ex); // Redis fora do ar nao pode derrubar a ingestao: cai para o banco
        }

        var snapshot = await LoadAsync(request.TenantId, cancellationToken).ConfigureAwait(false);

        try
        {
            await cache.SetAsync(key, new TenantSnapshotCacheEntry(snapshot), snapshot is null ? NegativeTtl : PositiveTtl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            LogCacheUnavailable(ex);
        }

        return snapshot;
    }

    private async Task<TenantSnapshot?> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.IsActive })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return null;
        }

        // Tenant confirmado: o escopo isolado passa a pertencer a ele para a leitura protegida por RLS.
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);

        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return new TenantSnapshot(
            tenantId,
            tenant.IsActive,
            settings?.HighRiskThreshold ?? Domain.TenantSettings.DefaultHighRiskThreshold,
            settings?.AutoBlockEnabled ?? false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis indisponivel ao ler/gravar o snapshot do tenant; usando o banco.")]
    private partial void LogCacheUnavailable(Exception exception);
}
