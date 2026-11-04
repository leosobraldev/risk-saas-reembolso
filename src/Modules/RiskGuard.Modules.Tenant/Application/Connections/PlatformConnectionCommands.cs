using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Modules.Tenant.Application.Audit;
using RiskGuard.Modules.Tenant.Domain;
using RiskGuard.Modules.Tenant.Persistence;

namespace RiskGuard.Modules.Tenant.Application.Connections;

/// <summary>Visao segura de uma conexao: nunca inclui segredos.</summary>
public sealed record PlatformConnectionDto(string Platform, string AccountId, bool HasAccessToken, DateTimeOffset UpdatedAt);

/// <summary>
/// Conecta (ou reconecta) a loja do lojista autenticado a uma plataforma. <c>WebhookSecret</c> e obrigatorio: sem ele os
/// webhooks da plataforma sao recusados (fail-closed). <c>AccessToken</c> e necessario para as acoes na API da loja.
/// </summary>
public sealed record UpsertPlatformConnectionCommand(string Platform, string? AccountId, string? WebhookSecret, string? AccessToken)
    : ICommand<Result<PlatformConnectionDto>>;

internal sealed class UpsertPlatformConnectionCommandHandler(
    TenantDbContext db,
    ITenantContext tenantContext,
    ISecretProtector protector,
    IActorContext actor,
    TimeProvider timeProvider) : IRequestHandler<UpsertPlatformConnectionCommand, Result<PlatformConnectionDto>>
{
    private const int MaxSecretLength = 1000;

    public async Task<Result<PlatformConnectionDto>> Handle(UpsertPlatformConnectionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is not { } tenantId)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        if (!PlatformIds.IsSupported(request.Platform))
        {
            return new Error("connection.unsupported_platform", "Plataforma nao suportada.", ErrorType.NotFound);
        }

        var platform = request.Platform.Trim().ToLowerInvariant();

        if (!PlatformIds.IsValidAccountId(platform, request.AccountId))
        {
            return new Error(
                "connection.invalid_account",
                platform == PlatformIds.Shopify
                    ? "Informe o dominio da loja no formato minha-loja.myshopify.com."
                    : "Informe o id numerico da loja.",
                ErrorType.Validation);
        }

        if (string.IsNullOrWhiteSpace(request.WebhookSecret) || request.WebhookSecret.Length > MaxSecretLength
            || request.AccessToken is { Length: > MaxSecretLength })
        {
            return new Error("connection.invalid_secret", "Informe o segredo do webhook (e um token de acesso com ate 1000 caracteres).", ErrorType.Validation);
        }

        var now = timeProvider.GetUtcNow();
        var webhookSecret = protector.Protect(request.WebhookSecret, PlatformConnection.WebhookSecretContext(tenantId, platform));
        var accessToken = string.IsNullOrWhiteSpace(request.AccessToken)
            ? null
            : protector.Protect(request.AccessToken, PlatformConnection.AccessTokenContext(tenantId, platform));

        var existing = await db.PlatformConnections.FirstOrDefaultAsync(c => c.Platform == platform, cancellationToken).ConfigureAwait(false);
        var accountId = request.AccountId!.Trim();

        PlatformConnection connection;
        if (existing is null)
        {
            connection = PlatformConnection.Create(tenantId, platform, accountId, webhookSecret, accessToken, now);
            db.PlatformConnections.Add(connection);
        }
        else
        {
            existing.Replace(accountId, webhookSecret, accessToken, now);
            connection = existing;
        }

        // Sem segredos no rastro: so a plataforma, a loja (identificador publico) e se houve token de acesso.
        AuditTrail.Record(
            db, actor, timeProvider, tenantId, AuditActions.ConnectionUpserted, platform,
            $"{(existing is null ? "criada" : "substituida")}; loja {accountId}; token de acesso {(accessToken is null ? "ausente" : "informado")}");

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new Error("connection.conflict", "A conexao foi alterada simultaneamente; tente novamente.", ErrorType.Conflict);
        }

        return new PlatformConnectionDto(connection.Platform, connection.AccountId, connection.AccessTokenProtected is not null, connection.UpdatedAt);
    }
}

public sealed record RemovePlatformConnectionCommand(string Platform) : ICommand<Result>;

internal sealed class RemovePlatformConnectionCommandHandler(
    TenantDbContext db,
    ITenantContext tenantContext,
    IActorContext actor,
    TimeProvider timeProvider) : IRequestHandler<RemovePlatformConnectionCommand, Result>
{
    public async Task<Result> Handle(RemovePlatformConnectionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized));
        }

        var platform = (request.Platform ?? string.Empty).Trim().ToLowerInvariant();
        var connection = await db.PlatformConnections.FirstOrDefaultAsync(c => c.Platform == platform, cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return Result.Failure(new Error("connection.not_found", "Conexao nao encontrada.", ErrorType.NotFound));
        }

        db.PlatformConnections.Remove(connection);
        AuditTrail.Record(db, actor, timeProvider, tenantId, AuditActions.ConnectionRemoved, platform, $"loja {connection.AccountId}");
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

public sealed record ListPlatformConnectionsQuery : IQuery<Result<IReadOnlyList<PlatformConnectionDto>>>;

internal sealed class ListPlatformConnectionsQueryHandler(TenantDbContext db, ITenantContext tenantContext)
    : IRequestHandler<ListPlatformConnectionsQuery, Result<IReadOnlyList<PlatformConnectionDto>>>
{
    public async Task<Result<IReadOnlyList<PlatformConnectionDto>>> Handle(ListPlatformConnectionsQuery request, CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return new Error("auth.no_tenant", "Requisicao sem tenant.", ErrorType.Unauthorized);
        }

        var items = await db.PlatformConnections.AsNoTracking()
            .OrderBy(c => c.Platform)
            .Select(c => new PlatformConnectionDto(c.Platform, c.AccountId, c.AccessTokenProtected != null, c.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<PlatformConnectionDto>>.Success(items);
    }
}
