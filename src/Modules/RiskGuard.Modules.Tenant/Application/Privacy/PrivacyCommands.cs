using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Core.Domain;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Privacy;

/// <summary>Contrato de ids de tenants para tarefas de manutencao (Tenants nao tem RLS: e o diretorio).</summary>
internal sealed class ListTenantIdsQueryHandler(TenantDbContext db) : IRequestHandler<ListTenantIdsQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(ListTenantIdsQuery request, CancellationToken cancellationToken) =>
        await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>
/// POST /api/v1/privacy/customers/anonymize: o lojista (controlador) pede a eliminacao dos dados de um comprador. O RiskGuard e
/// operador: cada modulo anonimiza o proprio schema ao receber o evento.
/// </summary>
public sealed record AnonymizeCustomerCommand(string? Email) : ICommand<Result>;

internal sealed partial class AnonymizeCustomerCommandHandler(
    IPublisher publisher,
    ITenantContext tenantContext,
    ILogger<AnonymizeCustomerCommandHandler> logger) : IRequestHandler<AnonymizeCustomerCommand, Result>
{
    private readonly ILogger<AnonymizeCustomerCommandHandler> _logger = logger;

    public async Task<Result> Handle(AnonymizeCustomerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized));
        }

        if (!EmailRules.IsValid(request.Email))
        {
            return Result.Failure(new Error("privacy.invalid_email", "Informe um e-mail valido.", ErrorType.Validation));
        }

        await publisher.Publish(new CustomerAnonymizationRequested(tenantId, EmailRules.Normalize(request.Email)), cancellationToken).ConfigureAwait(false);

        // trilha de auditoria SEM o e-mail (o dado que acabou de ser eliminado nao pode ir parar no log)
        LogAnonymizationRequested(tenantId);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Anonimizacao de comprador solicitada pelo tenant {TenantId}.")]
    private partial void LogAnonymizationRequested(Guid tenantId);
}
