using MediatR;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Ingestion.Persistence;

namespace RiskGuard.Modules.Ingestion.Application.Retention;

/// <summary>Anonimiza o comprador nos pedidos e apaga os payloads brutos que o citam (dados do schema <c>ingestion</c>).</summary>
internal sealed class AnonymizeCustomerIngestionHandler(IngestionDbContext db, ITenantContextSetter tenantContext, TimeProvider timeProvider)
    : INotificationHandler<CustomerAnonymizationRequested>
{
    public async Task Handle(CustomerAnonymizationRequested notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        tenantContext.SetTenant(notification.TenantId);
        var email = notification.CustomerEmail;
        var token = $"anonymized-{Guid.NewGuid():N}"; // aleatorio: nao ha como reverter nem ligar de volta ao e-mail
        var now = timeProvider.GetUtcNow();

        await db.Orders
            .Where(o => o.CustomerEmail == email)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.CustomerEmail, token), cancellationToken)
            .ConfigureAwait(false);

        // O payload e jsonb: busca textual, sem diferenciar maiusculas. %, _ e \ do e-mail sao escapados (nao viram curinga).
        var pattern = "%" + EscapeLike(email) + "%";
        const string emptyJson = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE ingestion."RawWebhooks"
            SET "Payload" = CAST({emptyJson} AS jsonb), "Headers" = CAST({emptyJson} AS jsonb), "PayloadPurgedAt" = {now}
            WHERE "TenantId" = {notification.TenantId} AND "PayloadPurgedAt" IS NULL AND "Payload"::text ILIKE {pattern} ESCAPE '\'
            """,
            cancellationToken).ConfigureAwait(false);
    }

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
