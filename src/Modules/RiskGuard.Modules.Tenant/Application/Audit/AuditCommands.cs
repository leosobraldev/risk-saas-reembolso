using MediatR;
using Microsoft.EntityFrameworkCore;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Tenant.Domain;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Audit;

/// <summary>
/// Atende o <see cref="RecordAuditEventCommand"/> enviado pelos outros modulos. O tenant do evento vira o tenant do escopo
/// (o escopo nunca troca de tenant: um evento para outro lojista dentro de uma requisicao e recusado pelo <c>SetTenant</c>).
/// </summary>
internal sealed class RecordAuditEventCommandHandler(
    TenantDbContext db,
    ITenantContextSetter tenantContext,
    IActorContext actor,
    TimeProvider timeProvider) : IRequestHandler<RecordAuditEventCommand, Result>
{
    public async Task<Result> Handle(RecordAuditEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.Action))
        {
            return Result.Failure(new Error("audit.invalid_event", "Evento de auditoria sem tenant ou sem acao.", ErrorType.Validation));
        }

        tenantContext.SetTenant(request.TenantId);
        db.AuditEvents.Add(AuditEvent.Create(request.TenantId, actor.ActorId, request.Action, request.Target, request.Details, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Usado pelos comandos do proprio modulo: o evento entra no MESMO SaveChanges da mudanca (atomico: nao ha mudanca sem rastro).</summary>
internal static class AuditTrail
{
    public static void Record(TenantDbContext db, IActorContext actor, TimeProvider timeProvider, Guid tenantId, string action, string? target = null, string? details = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        db.AuditEvents.Add(AuditEvent.Create(tenantId, actor.ActorId, action, target, details, timeProvider.GetUtcNow()));
    }
}
