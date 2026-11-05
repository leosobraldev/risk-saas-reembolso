using System.Text;
using RiskGuard.Modules.Ingestion.Application.Signatures;

namespace RiskGuard.UnitTests.Ingestion;

/// <summary>
/// Wake nao assina o corpo: ao inscrever o webhook o lojista cadastra um header proprio, que chega em toda notificacao. A
/// autenticidade e esse token compartilhado (o segredo da conexao), comparado em tempo constante.
/// </summary>
public class SharedTokenSignatureTests
{
    private const string Secret = "token-compartilhado-123";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"topico":"pedido.novo"}""");
    private readonly WakeTokenVerifier _verifier = new();

    private static Dictionary<string, string> Headers(string name, string value) => new() { [name] = value };

    [Fact]
    public void Platform_is_wake_and_the_header_is_documented()
    {
        _verifier.Platform.Should().Be("wake");
        WakeTokenVerifier.HeaderName.Should().Be("X-RiskGuard-Token");
    }

    [Fact]
    public void Accepts_the_matching_token()
    {
        _verifier.IsValid(Headers("X-RiskGuard-Token", Secret), Body, Secret).Should().BeTrue();
    }

    [Theory]
    [InlineData("x-riskguard-token")]
    [InlineData("X-RISKGUARD-TOKEN")]
    public void Header_name_is_case_insensitive(string name)
    {
        _verifier.IsValid(Headers(name, Secret), Body, Secret).Should().BeTrue();
    }

    [Fact]
    public void Surrounding_whitespace_in_the_header_value_is_tolerated()
    {
        _verifier.IsValid(Headers("X-RiskGuard-Token", $"  {Secret}\r\n"), Body, Secret).Should().BeTrue();
    }

    [Theory]
    [InlineData("outro-token")]
    [InlineData("token-compartilhado-12")]
    [InlineData("token-compartilhado-1234")]
    [InlineData("TOKEN-COMPARTILHADO-123")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_wrong_or_empty_token(string token)
    {
        _verifier.IsValid(Headers("X-RiskGuard-Token", token), Body, Secret).Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_missing_header()
    {
        _verifier.IsValid(new Dictionary<string, string>(), Body, Secret).Should().BeFalse();
    }

    [Fact]
    public void A_different_header_does_not_count()
    {
        _verifier.IsValid(Headers("Authorization", Secret), Body, Secret).Should().BeFalse();
        _verifier.IsValid(Headers("X-Shopify-Hmac-SHA256", Secret), Body, Secret).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Without_a_secret_nothing_is_valid(string? secret)
    {
        // fail-closed: conexao sem segredo nunca autentica, nem com header vazio
        _verifier.IsValid(Headers("X-RiskGuard-Token", string.Empty), Body, secret!).Should().BeFalse();
        _verifier.IsValid(Headers("X-RiskGuard-Token", "qualquer"), Body, secret!).Should().BeFalse();
    }

    [Fact]
    public void The_body_does_not_matter_only_the_token_does()
    {
        _verifier.IsValid(Headers("X-RiskGuard-Token", Secret), Encoding.UTF8.GetBytes("outro corpo"), Secret).Should().BeTrue();
    }
}
