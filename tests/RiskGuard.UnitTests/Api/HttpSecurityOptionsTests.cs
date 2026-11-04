using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using RiskGuard.Api.Security;

namespace RiskGuard.UnitTests.Api;

public class HttpSecurityOptionsTests
{
    private static HttpSecurityOptions Load(Dictionary<string, string?> values) =>
        HttpSecurityOptions.From(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Defaults_are_fail_closed()
    {
        var options = Load([]);

        options.TrustsProxies.Should().BeFalse();
        options.AllowedOrigins.Should().BeEmpty();
        options.RedirectToHttps.Should().BeFalse();
        options.BuildForwardedHeadersOptions().Should().BeNull(); // X-Forwarded-* ignorados: nenhum cliente forja o proprio IP
        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Configured_proxies_and_networks_are_the_only_trusted_ones()
    {
        var options = Load(new()
        {
            ["Proxy:KnownProxies:0"] = "10.0.0.5",
            ["Proxy:KnownNetworks:0"] = "172.16.0.0/12",
        });

        var forwarded = options.BuildForwardedHeadersOptions()!;

        forwarded.KnownProxies.Select(p => p.ToString()).Should().Equal("10.0.0.5"); // loopback NAO e confiado implicitamente
        forwarded.KnownNetworks.Should().ContainSingle();
        forwarded.ForwardLimit.Should().Be(1);
        forwarded.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    }

    [Theory]
    [InlineData("Proxy:KnownProxies:0", "not-an-ip")]
    [InlineData("Proxy:KnownProxies:0", "10.0.0.1/8")]
    [InlineData("Proxy:KnownNetworks:0", "10.0.0.0")]
    [InlineData("Proxy:KnownNetworks:0", "10.0.0.0/33")]
    [InlineData("Proxy:KnownNetworks:0", "10.0.0.0/-1")]
    [InlineData("Proxy:KnownNetworks:0", "nope/8")]
    public void Invalid_proxy_configuration_fails_at_startup(string key, string value)
    {
        var options = Load(new() { [key] = value });

        options.Invoking(o => o.Validate()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("https://app.riskguard.com.br")]
    [InlineData("http://localhost:3000")]
    [InlineData("https://app.example.com:8443")]
    public void Valid_origins_are_accepted(string origin)
    {
        Load(new() { ["Cors:AllowedOrigins:0"] = origin }).Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("https://app.example.com/")] // barra final: o navegador nunca envia assim, a politica nunca casaria
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com?x=1")]
    [InlineData("ftp://app.example.com")]
    [InlineData("app.example.com")]
    [InlineData("")]
    public void Wildcards_paths_and_non_http_origins_are_rejected(string origin)
    {
        Load(new() { ["Cors:AllowedOrigins:0"] = origin }).Invoking(o => o.Validate()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Https_redirect_is_opt_in()
    {
        Load(new() { ["Https:RedirectToHttps"] = "true" }).RedirectToHttps.Should().BeTrue();
    }

    [Fact]
    public void IPv6_networks_are_supported()
    {
        var options = Load(new() { ["Proxy:KnownNetworks:0"] = "fd00::/8" });

        options.Invoking(o => o.Validate()).Should().NotThrow();
        options.BuildForwardedHeadersOptions()!.KnownNetworks.Should().ContainSingle();
    }

    [Fact]
    public void Null_configuration_is_rejected()
    {
        ((Action)(() => HttpSecurityOptions.From(null!))).Should().Throw<ArgumentNullException>();
    }
}
