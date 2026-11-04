namespace RiskGuard.Core.Abstractions;

public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    RateLimited,
    Unavailable,
}

/// <summary>Falha esperada de negocio (nao e excecao).</summary>
public sealed record Error(string Code, string Message, ErrorType Type, TimeSpan? RetryAfter = null);

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    private readonly Error? _error;

    public bool IsSuccess { get; }

    public Error Error => _error ?? throw new InvalidOperationException("Resultado de sucesso nao possui erro.");

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Resultado de falha nao possui valor.");

    public static Result<T> Success(T value) => new(value, true, null);

    public static new Result<T> Failure(Error error) => new(default, false, error);

    public static implicit operator Result<T>(Error error) => Failure(error);

    public static implicit operator Result<T>(T value) => Success(value);
}
