using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Tenant.Application.Audit;
using RiskGuard.Modules.Tenant.Domain;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Commands;

/// <summary>RF01: cadastro de lojista com configuracoes padrao e provisionamento do usuario dono no provedor de identidade.</summary>
public sealed record RegisterTenantCommand(string Name, string Email) : ICommand<Result<RegisteredTenant>>;

/// <param name="PasswordSetupUrl">Link de uso unico para o dono definir a senha. E uma credencial: entregue so a ele e nao logue.</param>
public sealed record RegisteredTenant(Guid TenantId, string ExternalUserId, Uri? PasswordSetupUrl);

/// <remarks>
/// O banco e o provedor nao sao transacionais entre si. Ordem escolhida: (1) cria o usuario no provedor, (2) grava lojista,
/// configuracoes e vinculo em UMA transacao do banco; se (2) falhar, (3) o usuario do provedor e removido (compensacao). O pior caso
/// (a compensacao tambem falhar) deixa um usuario orfao no provedor cujo token aponta para um lojista inexistente: o middleware de
/// tenant ativo o recusa, entao ele nao acessa nada.
/// </remarks>
internal sealed partial class RegisterTenantCommandHandler(
    TenantDbContext db,
    IIdentityProvider identityProvider,
    ITenantContextSetter tenantContextSetter,
    IActorContext actor,
    TimeProvider timeProvider,
    ILogger<RegisterTenantCommandHandler> logger) : IRequestHandler<RegisterTenantCommand, Result<RegisteredTenant>>
{
    private static readonly Error EmailTaken =
        new("tenant.email_already_registered", "Ja existe um lojista com este e-mail.", ErrorType.Conflict);

    private readonly ILogger<RegisterTenantCommandHandler> _logger = logger;

    public async Task<Result<RegisteredTenant>> Handle(RegisterTenantCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var created = Domain.Tenant.Create(request.Name, request.Email, now);
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        var tenant = created.Value;

        if (await db.Tenants.AnyAsync(t => t.Email == tenant.Email, cancellationToken).ConfigureAwait(false))
        {
            return EmailTaken;
        }

        // 1) provedor de identidade
        var identity = await identityProvider
            .CreateUserAsync(new CreateIdentityUserRequest(tenant.Email, tenant.Name, tenant.Id), cancellationToken)
            .ConfigureAwait(false);
        if (!identity.IsSuccess)
        {
            return identity.Error;
        }

        var user = identity.Value;

        // 2) banco: tenant + configuracoes + vinculo, atomicos. O tenant do escopo e definido ANTES de abrir a conexao (RLS).
        tenantContextSetter.SetTenant(tenant.Id);
        db.Tenants.Add(tenant);
        db.Settings.Add(TenantSettings.CreateDefault(tenant.Id));
        db.TenantUsers.Add(TenantUser.CreateOwner(tenant.Id, user.ExternalId, tenant.Email, now));
        AuditTrail.Record(db, actor, timeProvider, tenant.Id, AuditActions.TenantRegistered, "tenant");

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await CompensateAsync(user.ExternalId).ConfigureAwait(false);
            return EmailTaken; // corrida entre dois cadastros simultaneos do mesmo e-mail
        }
        catch
        {
            await CompensateAsync(user.ExternalId).ConfigureAwait(false);
            throw;
        }

        return new RegisteredTenant(tenant.Id, user.ExternalId, user.PasswordSetupUrl);
    }

    private async Task CompensateAsync(string externalUserId)
    {
        // Compensacao independe do token de cancelamento da requisicao: se o cliente desistiu, ainda assim limpamos.
        var removed = await identityProvider.DeleteUserAsync(externalUserId, CancellationToken.None).ConfigureAwait(false);
        if (!removed.IsSuccess)
        {
            LogCompensationFailed(externalUserId, removed.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao remover o usuario {ExternalUserId} do provedor apos erro no cadastro ({ErrorCode}); remova-o manualmente.")]
    private partial void LogCompensationFailed(string externalUserId, string errorCode);
}
