using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RiskGuard.Infrastructure.Identity.Auth0;

internal sealed record ManagementResponse(HttpStatusCode Status, string Body);

/// <summary>Falha ao falar com o Auth0 (rede, timeout ou credenciais da Management API recusadas).</summary>
public sealed class Auth0UnavailableException : Exception
{
    public Auth0UnavailableException(string message)
        : base(message)
    {
    }

    public Auth0UnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Auth0UnavailableException()
    {
    }
}

/// <summary>
/// Cliente HTTP da Management API do Auth0 (client credentials). O token de acesso e obtido uma vez e reutilizado ate perto do
/// vencimento, para nao chamar /oauth/token a cada operacao (o Auth0 limita essas chamadas).
/// </summary>
internal sealed class Auth0ManagementClient(IHttpClientFactory httpClientFactory, IOptions<IdentityOptions> options, TimeProvider timeProvider) : IDisposable
{
    public const string HttpClientName = "auth0";

    private static readonly TimeSpan RenewMargin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    private Auth0Options Settings => options.Value.Auth0;

    public void Dispose() => _tokenLock.Dispose();

    public async Task<ManagementResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(method, new Uri($"https://{Settings.Domain}/api/v2/{path}"));
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && timeProvider.GetUtcNow() < _tokenExpiresAt - RenewMargin)
        {
            return _token;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is not null && timeProvider.GetUtcNow() < _tokenExpiresAt - RenewMargin)
            {
                return _token; // outro chamador renovou enquanto esperavamos
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://{Settings.Domain}/oauth/token"))
            {
                Content = JsonContent.Create(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = Settings.ClientId,
                    ["client_secret"] = Settings.ClientSecret,
                    ["audience"] = $"https://{Settings.Domain}/api/v2/",
                }),
            };

            var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.Status != HttpStatusCode.OK)
            {
                // 401/403: client_id/secret errados ou aplicacao nao autorizada na Management API. Nunca ecoar o corpo (pode citar o client).
                throw new Auth0UnavailableException($"o Auth0 recusou as credenciais da Management API (HTTP {(int)response.Status}).");
            }

            using var document = JsonDocument.Parse(response.Body);
            _token = document.RootElement.GetProperty("access_token").GetString()
                ?? throw new Auth0UnavailableException("resposta de token sem access_token.");
            var seconds = document.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
            _tokenExpiresAt = timeProvider.GetUtcNow().AddSeconds(seconds);
            return _token;
        }
        catch (JsonException ex)
        {
            throw new Auth0UnavailableException("resposta de token invalida.", ex);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<ManagementResponse> ExecuteAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return new ManagementResponse(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            throw new Auth0UnavailableException("falha de rede ao chamar o Auth0.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Auth0UnavailableException("timeout ao chamar o Auth0.", ex);
        }
    }
}
