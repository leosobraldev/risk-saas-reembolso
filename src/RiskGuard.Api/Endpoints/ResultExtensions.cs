using RiskGuard.Core.Abstractions;

namespace RiskGuard.Api.Endpoints;

internal static class ResultExtensions
{
    /// <summary>Converte falhas de negocio em ProblemDetails (RFC 9457) com o codigo estavel do erro.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : ToProblem(result.Error);
    }

    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess() : ToProblem(result.Error);
    }

    private static IResult ToProblem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = Results.Problem(
            statusCode: status,
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });

        return error.RetryAfter is { } retryAfter ? new WithRetryAfter(problem, retryAfter) : problem;
    }

    private sealed class WithRetryAfter(IResult inner, TimeSpan retryAfter) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
