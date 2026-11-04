using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Modules.Action.Application.Blockers;
using RiskGuard.Modules.Action.Domain;
using RiskGuard.Modules.Action.Persistence;

namespace RiskGuard.Modules.Action.Application.Commands;

/// <summary>
/// Executado pelo Hangfire: aplica (acao pendente) ou desfaz (acao em liberacao) o bloqueio na API da plataforma, com as
/// credenciais do lojista. Idempotente: acoes ja encerradas nao fazem nada.
/// </summary>
public sealed record ExecuteOrderActionCommand(Guid TenantId, Guid ActionId) : ICommand<Result>;

internal sealed class ExecuteOrderActionCommandHandler(
    ActionDbContext db,
    IOrderBlockerResolver blockers,
    ISender sender,
    ITenantContextSetter tenantContext,
    TimeProvider timeProvider) : IRequestHandler<ExecuteOrderActionCommand, Result>
{
    public async Task<Result> Handle(ExecuteOrderActionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // O job roda fora de uma requisicao HTTP: o tenant vem dos argumentos do job (e a RLS garante o isolamento).
        tenantContext.SetTenant(request.TenantId);

        var action = await db.OrderActions.FirstOrDefaultAsync(a => a.Id == request.ActionId, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Result.Failure(new Error("action.not_found", "Acao nao encontrada.", ErrorType.NotFound));
        }

        if (!action.IsPending && !action.IsReleasing)
        {
            return Result.Success(); // reexecucao de um job ja concluido
        }

        var releasing = action.IsReleasing;
        var outcome = await CallPlatformAsync(request.TenantId, action, releasing, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        switch (outcome.Status)
        {
            case ExternalApiStatus.Succeeded when releasing:
                action.MarkReleased(now);
                break;
            case ExternalApiStatus.Succeeded:
                action.MarkSucceeded(now);
                break;
            case ExternalApiStatus.PermanentFailure when releasing:
                action.MarkReleaseFailed(outcome.Error ?? "Falha permanente.", now);
                break;
            case ExternalApiStatus.PermanentFailure:
                action.MarkFailed(outcome.Error ?? "Falha permanente.", now);
                break;
            default:
                throw new TransientExternalApiException(outcome.Error ?? "Falha transitoria.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<ExternalApiResult> CallPlatformAsync(Guid tenantId, OrderAction action, bool releasing, CancellationToken cancellationToken)
    {
        var blocker = blockers.Resolve(action.Platform);
        if (blocker is null)
        {
            return ExternalApiResult.Permanent($"Nao ha integracao para a plataforma '{action.Platform}'.");
        }

        var credentials = await sender.Send(new GetPlatformCredentialsQuery(tenantId, action.Platform), cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            return ExternalApiResult.Permanent($"O lojista nao conectou a plataforma '{action.Platform}'.");
        }

        var blockRequest = new BlockRequest(
            action.Id,
            action.ExternalOrderId,
            action.Score,
            $"score de risco {action.Score} acima do limite configurado pelo lojista.",
            credentials);

        return releasing
            ? await blocker.ReleaseAsync(blockRequest, cancellationToken).ConfigureAwait(false)
            : await blocker.BlockAsync(blockRequest, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Registra uma tentativa que falhou (RNF04); esgotadas as tentativas, a acao vira Failed (ou ReleaseFailed).</summary>
public sealed record RecordActionFailureCommand(Guid TenantId, Guid ActionId, string Error) : ICommand<Result>;

internal sealed class RecordActionFailureCommandHandler(
    ActionDbContext db,
    ITenantContextSetter tenantContext,
    IOptions<HangfireOptions> hangfireOptions,
    TimeProvider timeProvider) : IRequestHandler<RecordActionFailureCommand, Result>
{
    public async Task<Result> Handle(RecordActionFailureCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        tenantContext.SetTenant(request.TenantId);

        var action = await db.OrderActions.FirstOrDefaultAsync(a => a.Id == request.ActionId, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Result.Success();
        }

        var maxRetries = hangfireOptions.Value.MaxRetryAttempts;
        var now = timeProvider.GetUtcNow();
        if (action.IsPending)
        {
            action.RecordFailedAttempt(request.Error, maxRetries, now);
        }
        else if (action.IsReleasing)
        {
            action.RecordFailedReleaseAttempt(request.Error, maxRetries, now);
        }
        else
        {
            return Result.Success();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
