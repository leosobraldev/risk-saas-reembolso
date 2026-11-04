using Microsoft.AspNetCore.Diagnostics;

namespace RiskGuard.Api.Middleware;

/// <summary>
/// Erros do CLIENTE detectados pelo framework (JSON malformado, parametro de query invalido, corpo grande demais) chegam como
/// <see cref="BadHttpRequestException"/> e devem manter o status 4xx. Sem este tratador, o <c>UseExceptionHandler</c> os
/// transformaria em 500 e confundiria erro de uso com falha do servidor. A mensagem interna do framework NAO e exposta.
/// Qualquer outra excecao segue para o tratador padrao (500 generico).
/// </summary>
internal sealed class ClientErrorExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not BadHttpRequestException bad)
        {
            return false;
        }

        httpContext.Response.StatusCode = bad.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = null, // nunca anexar a excecao interna
            ProblemDetails =
            {
                Status = bad.StatusCode,
                Title = "Requisicao invalida.",
            },
        });
    }
}
