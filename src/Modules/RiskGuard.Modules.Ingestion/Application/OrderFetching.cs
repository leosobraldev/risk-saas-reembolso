using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Infrastructure.Options;

namespace RiskGuard.Modules.Ingestion.Application;

/// <summary>
/// Plataformas cujo webhook traz so o id do pedido (ex.: Nuvemshop <c>{ store_id, event, id }</c>): o pedido completo e
/// buscado na API da loja com o token do lojista.
/// </summary>
public interface IPlatformOrderFetcher
{
    string Platform { get; }

    /// <returns>No sucesso, o JSON do pedido em <see cref="ExternalApiResult.Body"/>.</returns>
    Task<ExternalApiResult> FetchOrderAsync(PlatformCredentials credentials, string externalOrderId, CancellationToken cancellationToken = default);
}

public static class WebhookErrorCodes
{
    /// <summary>O payload e so uma notificacao (id); os dados do pedido precisam ser buscados na API da plataforma.</summary>
    public const string NeedsOrderFetch = "webhook.needs_order_fetch";
}

public sealed partial class NuvemshopOrderFetcher(IHttpClientFactory httpClientFactory, IOptions<PlatformApisOptions> options) : IPlatformOrderFetcher
{
    private static readonly Uri DefaultBase = new("https://api.nuvemshop.com.br/");

    public string Platform => "nuvemshop";

    public async Task<ExternalApiResult> FetchOrderAsync(PlatformCredentials credentials, string externalOrderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            return ExternalApiResult.Permanent("Lojista sem token de acesso da Nuvemshop: nao e possivel buscar o pedido.");
        }

        // Defesa em profundidade: so ids numericos entram na rota.
        if (!OrderId().IsMatch(externalOrderId) || !RiskGuard.Core.Abstractions.PlatformIds.IsValidAccountId(Platform, credentials.AccountId))
        {
            return ExternalApiResult.Permanent("Identificador de pedido ou de loja invalido.");
        }

        var root = options.Value.For(Platform).BaseUrl ?? DefaultBase;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, $"v1/{credentials.AccountId}/orders/{externalOrderId}"));
        request.Headers.TryAddWithoutValidation("Authentication", $"bearer {credentials.AccessToken}"); // header nao padrao, conforme a documentacao

        using var client = httpClientFactory.CreateClient(Platform);
        return await ExternalApiCaller.SendAsync(client, request, Platform, cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex OrderId();
}
