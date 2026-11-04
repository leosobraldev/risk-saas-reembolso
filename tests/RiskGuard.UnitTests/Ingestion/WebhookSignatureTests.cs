using System.Security.Cryptography;
using System.Text;
using RiskGuard.Modules.Ingestion.Application.Signatures;

namespace RiskGuard.UnitTests.Ingestion;

/// <summary>Autenticidade dos webhooks: HMAC-SHA256 do corpo CRU com o segredo do lojista (Shopify: base64; Nuvemshop: hex/base64).</summary>
public class WebhookSignatureTests
{
    private const string Secret = "segredo-do-lojista";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"id":1,"total_price":"10.00"}""");

    private static byte[] Hmac(byte[] body, string secret) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);

    private static Dictionary<string, string> Headers(string name, string value) => new() { [name] = value };

    // ---- Shopify ----

    [Fact]
    public void Shopify_accepts_the_correct_base64_signature()
    {
        var header = Headers("X-Shopify-Hmac-SHA256", Convert.ToBase64String(Hmac(Body, Secret)));

        new ShopifySignatureVerifier().IsValid(header, Body, Secret).Should().BeTrue();
    }

    [Theory]
    [InlineData("x-shopify-hmac-sha256")]
    [InlineData("X-SHOPIFY-HMAC-SHA256")]
    public void Header_names_are_case_insensitive(string name)
    {
        var header = Headers(name, Convert.ToBase64String(Hmac(Body, Secret)));

        new ShopifySignatureVerifier().IsValid(header, Body, Secret).Should().BeTrue();
    }

    [Fact]
    public void Shopify_rejects_a_tampered_body()
    {
        var header = Headers("X-Shopify-Hmac-SHA256", Convert.ToBase64String(Hmac(Body, Secret)));
        var tampered = Encoding.UTF8.GetBytes("""{"id":1,"total_price":"0.01"}""");

        new ShopifySignatureVerifier().IsValid(header, tampered, Secret).Should().BeFalse();
    }

    [Fact]
    public void A_single_bit_flip_anywhere_in_the_body_invalidates_the_signature()
    {
        var header = Headers("X-Shopify-Hmac-SHA256", Convert.ToBase64String(Hmac(Body, Secret)));
        var verifier = new ShopifySignatureVerifier();

        for (var i = 0; i < Body.Length; i++)
        {
            var copy = (byte[])Body.Clone();
            copy[i] ^= 0x01;
            verifier.IsValid(header, copy, Secret).Should().BeFalse($"byte {i}");
        }
    }

    [Fact]
    public void Shopify_rejects_a_signature_made_with_another_secret()
    {
        var header = Headers("X-Shopify-Hmac-SHA256", Convert.ToBase64String(Hmac(Body, "outro-segredo")));

        new ShopifySignatureVerifier().IsValid(header, Body, Secret).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")] // base64 valido, digest curto
    public void Shopify_rejects_missing_or_malformed_headers(string value)
    {
        new ShopifySignatureVerifier().IsValid(Headers("X-Shopify-Hmac-SHA256", value), Body, Secret).Should().BeFalse();
    }

    [Fact]
    public void Shopify_rejects_when_the_header_is_absent_or_belongs_to_another_platform()
    {
        var verifier = new ShopifySignatureVerifier();

        verifier.IsValid(new Dictionary<string, string>(), Body, Secret).Should().BeFalse();
        verifier.IsValid(Headers("x-linkedstore-hmac-sha256", Convert.ToHexString(Hmac(Body, Secret))), Body, Secret).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    public void An_empty_secret_never_validates(string secret)
    {
        // mesmo que o atacante assine com a chave vazia: sem segredo configurado, nada e aceito
        var header = Headers("X-Shopify-Hmac-SHA256", Convert.ToBase64String(Hmac(Body, secret)));

        new ShopifySignatureVerifier().IsValid(header, Body, secret).Should().BeFalse();
    }

    [Fact]
    public void An_empty_body_can_still_be_verified_but_only_with_the_matching_signature()
    {
        var header = Headers("X-Shopify-Hmac-SHA256", Convert.ToBase64String(Hmac([], Secret)));
        var verifier = new ShopifySignatureVerifier();

        verifier.IsValid(header, [], Secret).Should().BeTrue();
        verifier.IsValid(header, Body, Secret).Should().BeFalse();
    }

    [Fact]
    public void A_whitespace_padded_header_is_tolerated()
    {
        var header = Headers("X-Shopify-Hmac-SHA256", "  " + Convert.ToBase64String(Hmac(Body, Secret)) + " ");

        new ShopifySignatureVerifier().IsValid(header, Body, Secret).Should().BeTrue();
    }

    [Fact]
    public void Null_headers_are_rejected()
    {
        var act = () => new ShopifySignatureVerifier().IsValid(null!, Body, Secret);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Nuvemshop ----

    [Fact]
    public void Nuvemshop_accepts_a_hex_signature_in_either_case()
    {
        var hex = Convert.ToHexString(Hmac(Body, Secret));
        var verifier = new NuvemshopSignatureVerifier();

        verifier.IsValid(Headers("x-linkedstore-hmac-sha256", hex.ToLowerInvariant()), Body, Secret).Should().BeTrue();
        verifier.IsValid(Headers("X-Linkedstore-Hmac-Sha256", hex.ToUpperInvariant()), Body, Secret).Should().BeTrue();
    }

    [Fact]
    public void Nuvemshop_also_accepts_base64_since_the_docs_do_not_state_the_encoding()
    {
        var header = Headers("x-linkedstore-hmac-sha256", Convert.ToBase64String(Hmac(Body, Secret)));

        new NuvemshopSignatureVerifier().IsValid(header, Body, Secret).Should().BeTrue();
    }

    [Fact]
    public void Nuvemshop_rejects_tampered_wrong_secret_and_missing_header()
    {
        var verifier = new NuvemshopSignatureVerifier();
        var good = Convert.ToHexString(Hmac(Body, Secret));

        verifier.IsValid(Headers("x-linkedstore-hmac-sha256", good), Encoding.UTF8.GetBytes("{}"), Secret).Should().BeFalse();
        verifier.IsValid(Headers("x-linkedstore-hmac-sha256", good), Body, "errado").Should().BeFalse();
        verifier.IsValid(new Dictionary<string, string>(), Body, Secret).Should().BeFalse();
        verifier.IsValid(Headers("x-linkedstore-hmac-sha256", "zz-not-hex-nor-b64"), Body, Secret).Should().BeFalse();
    }

    [Fact]
    public void Platforms_are_stable_identifiers()
    {
        new ShopifySignatureVerifier().Platform.Should().Be("shopify");
        new NuvemshopSignatureVerifier().Platform.Should().Be("nuvemshop");
    }

    // ---- resolver ----

    [Theory]
    [InlineData("shopify")]
    [InlineData("SHOPIFY")]
    [InlineData("Nuvemshop")]
    public void Resolver_finds_verifiers_case_insensitively(string platform)
    {
        var resolver = new WebhookSignatureVerifierResolver([new ShopifySignatureVerifier(), new NuvemshopSignatureVerifier()]);

        resolver.Resolve(platform)!.Platform.Should().Be(platform.ToLowerInvariant());
    }

    [Theory]
    [InlineData("magento")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolver_returns_null_for_unknown_platforms(string? platform)
    {
        new WebhookSignatureVerifierResolver([new ShopifySignatureVerifier()]).Resolve(platform).Should().BeNull();
    }

    [Fact]
    public void Resolver_rejects_duplicates()
    {
        var act = () => new WebhookSignatureVerifierResolver([new ShopifySignatureVerifier(), new ShopifySignatureVerifier()]);

        act.Should().Throw<InvalidOperationException>();
    }
}
