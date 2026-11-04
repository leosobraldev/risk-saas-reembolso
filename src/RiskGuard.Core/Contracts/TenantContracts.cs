using RiskGuard.Core.Abstractions;

namespace RiskGuard.Core.Contracts;

/// <summary>Visao somente-leitura de um tenant para outros modulos (nunca expoe credenciais).</summary>
public sealed record TenantSnapshot(Guid TenantId, bool IsActive, int HighRiskThreshold, bool AutoBlockEnabled);

/// <summary>
/// Contrato entre modulos (via MediatR): quem precisa de dados do tenant pergunta ao TenantModule
/// em vez de ler o schema dele. Retorna <c>null</c> quando o tenant nao existe.
/// </summary>
public sealed record GetTenantSnapshotQuery(Guid TenantId) : IQuery<TenantSnapshot?>;
