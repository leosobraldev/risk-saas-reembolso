using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Tenant.Domain;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Connections;

/// <summary>
/// Responde ao contrato <see cref="GetPlatformCredentialsQuery"/>. Le em escopo proprio com o tenant consultado (o
/// chamador pode ser um webhook anonimo) e decifra em memoria. Sem cache: segredo em claro nunca vai para o Redis.
/// </summary>
internal sealed class GetPlatformCredentialsQueryHandler(IServiceScopeFactory scopeFactory)
    : IRequestHandler<GetPlatformCredentialsQuery, PlatformCredentials?>
{
    public async Task<PlatformCredentials?> Handle(GetPlatformCredentialsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var platform = request.Platform.Trim().ToLowerInvariant();

        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(request.TenantId);

        var connection = await scope.ServiceProvider.GetRequiredService<TenantDbContext>()
            .PlatformConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Platform == platform, cancellationToken)
            .ConfigureAwait(false);

        if (connection is null)
        {
            return null;
        }

        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        return new PlatformCredentials(
            connection.Platform,
            connection.AccountId,
            protector.Unprotect(connection.WebhookSecretProtected, PlatformConnection.WebhookSecretContext(request.TenantId, platform)),
            connection.AccessTokenProtected is null
                ? null
                : protector.Unprotect(connection.AccessTokenProtected, PlatformConnection.AccessTokenContext(request.TenantId, platform)));
    }
}
