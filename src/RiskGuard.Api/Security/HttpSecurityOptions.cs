using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RiskGuard.Api.Security;

/// <summary>
/// Configuracao de borda HTTP. Tudo e opt-in e fail-closed: sem <c>Proxy</c> os headers X-Forwarded-* sao IGNORADOS (senao
/// qualquer cliente forjaria o proprio IP e escaparia do rate limit) e sem <c>Cors</c> nenhuma origem externa e aceita.
/// </summary>
public sealed class HttpSecurityOptions
{
    /// <summary>IPs dos proxies/load balancers que podem informar o IP real do cliente.</summary>
    public IReadOnlyList<string> KnownProxies { get; init; } = [];

    /// <summary>Redes (CIDR, ex.: 10.0.0.0/8) de proxies confiaveis.</summary>
    public IReadOnlyList<string> KnownNetworks { get; init; } = [];

    /// <summary>Origens do frontend (esquema + host [+ porta]), sem curinga.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>Redireciona HTTP para HTTPS. Atras de proxy que termina TLS exige Proxy configurado (para enxergar o esquema original).</summary>
    public bool RedirectToHttps { get; init; }

    public static HttpSecurityOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new HttpSecurityOptions
        {
            KnownProxies = configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [],
            KnownNetworks = configuration.GetSection("Proxy:KnownNetworks").Get<string[]>() ?? [],
            AllowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [],
            RedirectToHttps = configuration.GetValue<bool>("Https:RedirectToHttps"),
        };
    }

    public bool TrustsProxies => KnownProxies.Count > 0 || KnownNetworks.Count > 0;

    /// <summary>Falha na subida (fail-fast) se a configuracao for insegura ou invalida.</summary>
    public void Validate()
    {
        foreach (var proxy in KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                throw new InvalidOperationException($"Proxy:KnownProxies contem um IP invalido: '{proxy}'.");
            }
        }

        foreach (var network in KnownNetworks)
        {
            if (!TryParseNetwork(network, out _))
            {
                throw new InvalidOperationException($"Proxy:KnownNetworks contem uma rede CIDR invalida: '{network}'.");
            }
        }

        foreach (var origin in AllowedOrigins)
        {
            if (origin.Contains('*', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cors:AllowedOrigins nao aceita curingas.");
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || origin.EndsWith('/'))
            {
                throw new InvalidOperationException($"Cors:AllowedOrigins deve conter apenas origens (esquema://host[:porta]): '{origin}'.");
            }
        }
    }

    /// <returns><c>null</c> quando nenhum proxy e confiavel: o middleware de X-Forwarded-* nem deve ser instalado.</returns>
    public ForwardedHeadersOptions? BuildForwardedHeadersOptions()
    {
        if (!TrustsProxies)
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1, // so o salto imediato: o cliente nao consegue empilhar IPs forjados
        };

        // O padrao confia em loopback; aqui so vale o que foi configurado.
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var proxy in KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in KnownNetworks)
        {
            options.KnownNetworks.Add(TryParseNetwork(network, out var parsed)
                ? parsed!
                : throw new InvalidOperationException($"Rede CIDR invalida: '{network}'."));
        }

        return options;
    }

    private static bool TryParseNetwork(string cidr, out Microsoft.AspNetCore.HttpOverrides.IPNetwork? network)
    {
        network = null;
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length))
        {
            return false;
        }

        var max = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (length < 0 || length > max)
        {
            return false;
        }

        network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length);
        return true;
    }
}
