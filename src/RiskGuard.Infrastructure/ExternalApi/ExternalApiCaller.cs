using System.Net;

namespace RiskGuard.Infrastructure.ExternalApi;

public enum ExternalApiStatus
{
    Succeeded,

    /// <summary>Indisponibilidade ou limite da plataforma: vale tentar de novo (retry do Hangfire).</summary>
    TransientFailure,

    /// <summary>Rejeicao definitiva: repetir nao muda o resultado.</summary>
    PermanentFailure,
}

public sealed record ExternalApiResult(ExternalApiStatus Status, string? Error = null, string? Body = null)
{
    public bool IsSuccess => Status == ExternalApiStatus.Succeeded;

    public static ExternalApiResult Success(string? body = null) => new(ExternalApiStatus.Succeeded, null, body);

    public static ExternalApiResult Transient(string error) => new(ExternalApiStatus.TransientFailure, error);

    public static ExternalApiResult Permanent(string error) => new(ExternalApiStatus.PermanentFailure, error);
}

/// <summary>Chamada HTTP a plataformas externas com classificacao uniforme das respostas (transitoria x permanente).</summary>
public static class ExternalApiCaller
{
    public static async Task<ExternalApiResult> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        string platform,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var result = Classify(response.StatusCode, platform);
            if (!result.IsSuccess)
            {
                return result;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ExternalApiResult.Success(body);
        }
        catch (HttpRequestException ex)
        {
            return ExternalApiResult.Transient($"Falha de rede ao chamar '{platform}': {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ExternalApiResult.Transient($"Timeout ao chamar '{platform}'.");
        }
    }

    public static ExternalApiResult Classify(HttpStatusCode status, string platform)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
        {
            return ExternalApiResult.Success();
        }

        var description = $"A plataforma '{platform}' respondeu {code} {status}.";
        return status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500
            ? ExternalApiResult.Transient(description)
            : ExternalApiResult.Permanent(description);
    }
}
