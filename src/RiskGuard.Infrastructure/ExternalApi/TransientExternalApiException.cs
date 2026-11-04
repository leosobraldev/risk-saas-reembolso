namespace RiskGuard.Infrastructure.ExternalApi;

/// <summary>Falha transitoria de uma API externa: deve subir como excecao para o retry automatico do Hangfire (RNF04).</summary>
public sealed class TransientExternalApiException : Exception
{
    public TransientExternalApiException()
    {
    }

    public TransientExternalApiException(string message)
        : base(message)
    {
    }

    public TransientExternalApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
