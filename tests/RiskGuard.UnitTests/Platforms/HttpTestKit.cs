using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.Options;

namespace RiskGuard.UnitTests.Platforms;

internal sealed record SentRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body);

/// <summary>Plataforma falsa programavel: grava as requisicoes e responde pela funcao informada.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<SentRequest> Requests { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        Requests.Add(new SentRequest(
            request.Method,
            request.RequestUri!,
            headers,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return responder(request);
    }
}

internal static class HttpTestKit
{
    public static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    public static IOptions<PlatformApisOptions> Options(string? platform = null, string? baseUrl = null, string? apiVersion = null)
    {
        var options = new PlatformApisOptions();
        if (platform is not null)
        {
            options.Platforms[platform] = new PlatformApiEndpoint
            {
                BaseUrl = baseUrl is null ? null : new Uri(baseUrl),
                ApiVersion = apiVersion,
            };
        }

        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static PlatformCredentials Credentials(string platform, string accountId, string? token = "token-123") =>
        new(platform, accountId, "webhook-secret", token);
}
