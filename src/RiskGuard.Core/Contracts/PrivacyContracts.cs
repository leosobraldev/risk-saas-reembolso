using MediatR;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Core.Contracts;

/// <summary>
/// Pedido de anonimizacao dos dados de um comprador (direito de eliminacao, LGPD art. 18). Cada modulo que guarda dados pessoais
/// trata o PROPRIO schema ao receber este evento; nenhum modulo acessa o banco do outro.
/// </summary>
public sealed record CustomerAnonymizationRequested(Guid TenantId, string CustomerEmail) : INotification;

/// <summary>Ids de todos os lojistas (tarefas de manutencao que precisam percorrer os tenants, sob RLS, um a um).</summary>
public sealed record ListTenantIdsQuery : IQuery<IReadOnlyList<Guid>>;
