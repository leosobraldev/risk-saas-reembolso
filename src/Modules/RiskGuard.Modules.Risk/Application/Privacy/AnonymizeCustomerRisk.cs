using MediatR;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Risk.Persistence;

namespace RiskGuard.Modules.Risk.Application.Privacy;

/// <summary>Desvincula o historico de reembolsos do comprador (dados do schema <c>risk</c>); as avaliacoes nao guardam e-mail.</summary>
internal sealed class AnonymizeCustomerRiskHandler(RiskDbContext db, ITenantContextSetter tenantContext)
    : INotificationHandler<CustomerAnonymizationRequested>
{
    public async Task Handle(CustomerAnonymizationRequested notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        tenantContext.SetTenant(notification.TenantId);
        var email = notification.CustomerEmail;
        var token = $"anonymized-{Guid.NewGuid():N}";

        await db.CustomerHistories
            .Where(h => h.CustomerEmail == email)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.CustomerEmail, token), cancellationToken)
            .ConfigureAwait(false);
    }
}
