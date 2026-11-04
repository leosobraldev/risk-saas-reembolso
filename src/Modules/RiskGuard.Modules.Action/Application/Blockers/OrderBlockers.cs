using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Modules.Action.Domain;

namespace RiskGuard.Modules.Action.Application.Blockers;

/// <param name="ActionId">Identifica a acao (trilha de auditoria); nao e enviado como chave de idempotencia: a idempotencia vem do estado do pedido.</param>
public sealed record BlockRequest(Guid ActionId, string ExternalOrderId, int Score, string Reason, PlatformCredentials Credentials);

/// <summary>Strategy: como proteger (e liberar) um pedido na API de cada plataforma, com as credenciais do lojista.</summary>
public interface IOrderBlocker
{
    string Platform { get; }

    /// <summary>O que a plataforma permite: pausar o pedido (Block) ou apenas sinaliza-lo (Flag).</summary>
    OrderActionType ActionType { get; }

    Task<ExternalApiResult> BlockAsync(BlockRequest request, CancellationToken cancellationToken = default);

    /// <summary>Desfaz o que <see cref="BlockAsync"/> fez; idempotente (nada a desfazer = sucesso).</summary>
    Task<ExternalApiResult> ReleaseAsync(BlockRequest request, CancellationToken cancellationToken = default);
}

public interface IOrderBlockerResolver
{
    IOrderBlocker? Resolve(string? platform);
}

public sealed class OrderBlockerResolver : IOrderBlockerResolver
{
    private readonly Dictionary<string, IOrderBlocker> _blockers = new(StringComparer.OrdinalIgnoreCase);

    public OrderBlockerResolver(IEnumerable<IOrderBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);

        foreach (var blocker in blockers)
        {
            if (!_blockers.TryAdd(blocker.Platform, blocker))
            {
                throw new InvalidOperationException($"Integracao '{blocker.Platform}' registrada mais de uma vez.");
            }
        }
    }

    public IOrderBlocker? Resolve(string? platform) =>
        platform is not null && _blockers.TryGetValue(platform.Trim(), out var blocker) ? blocker : null;
}
