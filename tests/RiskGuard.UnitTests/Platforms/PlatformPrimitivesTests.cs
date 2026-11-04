using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Infrastructure.Security;
using RiskGuard.Modules.Action.Application.Blockers;
using RiskGuard.Modules.Ingestion.Application;
using static RiskGuard.UnitTests.Platforms.HttpTestKit;

namespace RiskGuard.UnitTests.Platforms;

public class PlatformIdsTests
{
    [Theory]
    [InlineData("shopify", true)]
    [InlineData("SHOPIFY", true)]
    [InlineData(" nuvemshop ", true)]
    [InlineData("magento", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_known_platforms_are_supported(string? platform, bool expected)
    {
        PlatformIds.IsSupported(platform).Should().Be(expected);
    }

    [Theory]
    [InlineData("shopify", "loja.myshopify.com", true)]
    [InlineData("shopify", "minha-loja-2.myshopify.com", true)]
    [InlineData("shopify", "loja.myshopify.com.evil.com", false)]
    [InlineData("shopify", "evil.com", false)]
    [InlineData("shopify", "https://loja.myshopify.com", false)]
    [InlineData("shopify", "LOJA.myshopify.com", false)]
    [InlineData("shopify", "-loja.myshopify.com", false)]
    [InlineData("shopify", "", false)]
    [InlineData("nuvemshop", "123456", true)]
    [InlineData("nuvemshop", "1", true)]
    [InlineData("nuvemshop", "12a", false)]
    [InlineData("nuvemshop", "1234567890123456", false)]
    [InlineData("nuvemshop", "-1", false)]
    [InlineData("magento", "123", false)]
    public void Account_ids_are_validated_per_platform(string platform, string accountId, bool expected)
    {
        PlatformIds.IsValidAccountId(platform, accountId).Should().Be(expected);
    }
}

public class ExternalApiCallerTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    public void Success_statuses(HttpStatusCode status)
    {
        ExternalApiCaller.Classify(status, "x").IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Throttling_and_server_errors_are_transient(HttpStatusCode status)
    {
        var result = ExternalApiCaller.Classify(status, "shopify");

        result.Status.Should().Be(ExternalApiStatus.TransientFailure);
        result.Error.Should().Contain(((int)status).ToString()).And.Contain("shopify");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public void Everything_else_is_permanent(HttpStatusCode status)
    {
        ExternalApiCaller.Classify(status, "x").Status.Should().Be(ExternalApiStatus.PermanentFailure);
    }

    [Fact]
    public async Task Timeouts_are_transient_but_caller_cancellation_is_not_swallowed()
    {
        using var client = new HttpClient(new FakeHttpHandler(_ => throw new TaskCanceledException("timeout")), disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://x.test/");

        var result = await ExternalApiCaller.SendAsync(client, request, "x", CancellationToken.None);
        result.Status.Should().Be(ExternalApiStatus.TransientFailure);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "https://x.test/");
        using var client2 = new HttpClient(new FakeHttpHandler(_ => throw new TaskCanceledException("c", null, cts.Token)), disposeHandler: false);
        await ((Func<Task>)(() => ExternalApiCaller.SendAsync(client2, request2, "x", cts.Token))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Success_returns_the_body()
    {
        using var client = new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, """{"a":1}""")), disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://x.test/");

        var result = await ExternalApiCaller.SendAsync(client, request, "x", CancellationToken.None);

        result.Body.Should().Be("""{"a":1}""");
    }
}

public class AesGcmSecretProtectorTests
{
    private static AesGcmSecretProtector Create(byte[]? key = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new SecurityOptions { EncryptionKey = Convert.ToBase64String(key ?? new byte[32]) });
        return new AesGcmSecretProtector(options);
    }

    [Fact]
    public void Roundtrips_and_never_contains_the_plaintext()
    {
        var protector = Create();

        var cipher = protector.Protect("shpat_super_secret", "ctx");

        cipher.Should().StartWith("v1:").And.NotContain("shpat_super_secret");
        protector.Unprotect(cipher, "ctx").Should().Be("shpat_super_secret");
    }

    [Fact]
    public void Same_plaintext_encrypts_differently_each_time()
    {
        var protector = Create();

        protector.Protect("x", "ctx").Should().NotBe(protector.Protect("x", "ctx"));
    }

    [Fact]
    public void A_value_only_decrypts_in_the_context_it_was_encrypted_for()
    {
        var protector = Create();
        var cipher = protector.Protect("segredo", "tenant-A:shopify:token");

        var act = () => protector.Unprotect(cipher, "tenant-B:shopify:token");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Another_key_cannot_decrypt()
    {
        var a = Create(new byte[32]);
        var b = Create([.. Enumerable.Repeat((byte)7, 32)]);

        var act = () => b.Unprotect(a.Protect("x", "ctx"), "ctx");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Tampered_corrupted_or_foreign_values_are_rejected()
    {
        var protector = Create();
        var cipher = protector.Protect("segredo", "ctx");
        var bytes = Convert.FromBase64String(cipher["v1:".Length..]);
        bytes[^1] ^= 0xFF;

        ((Action)(() => protector.Unprotect("v1:" + Convert.ToBase64String(bytes), "ctx"))).Should().Throw<CryptographicException>();
        ((Action)(() => protector.Unprotect("v1:!!!not-base64!!!", "ctx"))).Should().Throw<CryptographicException>();
        ((Action)(() => protector.Unprotect("v1:AAAA", "ctx"))).Should().Throw<CryptographicException>();
        ((Action)(() => protector.Unprotect("v2:" + Convert.ToBase64String(bytes), "ctx"))).Should().Throw<CryptographicException>();
        ((Action)(() => protector.Unprotect("plaintext", "ctx"))).Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Empty_and_unicode_secrets_work()
    {
        var protector = Create();

        protector.Unprotect(protector.Protect(string.Empty, "ctx"), "ctx").Should().BeEmpty();
        protector.Unprotect(protector.Protect("sengredo-ção-日本", "ctx"), "ctx").Should().Be("sengredo-ção-日本");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!!")]
    [InlineData("AAAA")] // 3 bytes
    public void Invalid_keys_are_rejected_at_startup(string key)
    {
        new SecurityOptions { EncryptionKey = key }.TryGetKeyBytes().Should().BeNull();
    }

    [Fact]
    public void A_32_byte_key_is_accepted()
    {
        new SecurityOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }.TryGetKeyBytes().Should().HaveCount(32);
    }
}

public class NuvemshopOrderFetcherTests
{
    private static PlatformCredentials Creds(string? token = "tok", string store = "123") => Credentials("nuvemshop", store, token);

    [Fact]
    public async Task Fetches_the_full_order_with_the_documented_headers()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(HttpStatusCode.OK, """{"id":4501}"""));
        var fetcher = new NuvemshopOrderFetcher(Factory(handler), Options());

        var result = await fetcher.FetchOrderAsync(Creds(), "4501");

        result.IsSuccess.Should().BeTrue();
        result.Body.Should().Be("""{"id":4501}""");
        handler.Requests.Single().Uri.AbsoluteUri.Should().Be("https://api.nuvemshop.com.br/v1/123/orders/4501");
        handler.Requests.Single().Headers["Authentication"].Should().Be("bearer tok");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ExternalApiStatus.PermanentFailure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ExternalApiStatus.TransientFailure)]
    public async Task Failures_are_classified(HttpStatusCode status, ExternalApiStatus expected)
    {
        var fetcher = new NuvemshopOrderFetcher(Factory(new FakeHttpHandler(_ => FakeHttpHandler.Status(status))), Options());

        (await fetcher.FetchOrderAsync(Creds(), "4501")).Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "123", "4501")]
    [InlineData("tok", "abc", "4501")]
    [InlineData("tok", "123", "4501/close")]
    [InlineData("tok", "123", "abc")]
    public async Task Invalid_input_fails_permanently_without_calling_out(string? token, string store, string orderId)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));
        var fetcher = new NuvemshopOrderFetcher(Factory(handler), Options());

        var result = await fetcher.FetchOrderAsync(Creds(token, store), orderId);

        result.Status.Should().Be(ExternalApiStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Platform_is_nuvemshop()
    {
        new NuvemshopOrderFetcher(Substitute.For<IHttpClientFactory>(), Options()).Platform.Should().Be("nuvemshop");
    }
}
