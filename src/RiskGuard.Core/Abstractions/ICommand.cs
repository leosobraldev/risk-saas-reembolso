using MediatR;

namespace RiskGuard.Core.Abstractions;

/// <summary>Comando CQRS: altera estado e retorna <typeparamref name="TResponse"/>.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>Consulta CQRS: nao altera estado.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;
