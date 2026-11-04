using RiskGuard.Modules.Risk.Application.Rules;
using static RiskGuard.UnitTests.Risk.RiskTestData;

namespace RiskGuard.UnitTests.Risk;

/// <summary>Criterio de aceite 2 (SDD 6): "fraude@teste.com" => SuspiciousEmailRule adiciona 50 pontos.</summary>
public class SuspiciousEmailRuleTests
{
    private readonly SuspiciousEmailRule _rule = new();

    [Fact]
    public void Acceptance_scenario_fraude_at_teste_adds_exactly_50_points()
    {
        var contribution = _rule.Evaluate(Context(email: "fraude@teste.com"));

        contribution.Should().NotBeNull();
        contribution!.Points.Should().Be(50);
        contribution.RuleName.Should().Be(nameof(SuspiciousEmailRule));
        contribution.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Rule_name_is_stable_for_audit_and_dashboards()
    {
        _rule.Name.Should().Be("SuspiciousEmailRule");
    }

    [Theory]
    [InlineData("fraude@x.com")]
    [InlineData("FRAUDE@x.com")]
    [InlineData("Fraude@x.com")]
    [InlineData("meu.fraude@x.com")]
    [InlineData("fraude123@x.com")]
    [InlineData("fraude+tag@x.com")]
    [InlineData("fraud@x.com")]
    [InlineData("golpe@x.com")]
    [InlineData("golpista99@x.com")]
    [InlineData("scam@x.com")]
    [InlineData("im.a.scammer@x.com")]
    [InlineData("  fraude@x.com  ")]
    public void Suspicious_keywords_in_the_local_part_are_flagged(string email)
    {
        _rule.Evaluate(Context(email: email))!.Points.Should().Be(50);
    }

    [Theory]
    [InlineData("a@mailinator.com")]
    [InlineData("a@guerrillamail.com")]
    [InlineData("a@10minutemail.com")]
    [InlineData("a@tempmail.com")]
    [InlineData("a@yopmail.com")]
    [InlineData("a@trashmail.com")]
    [InlineData("a@MAILINATOR.COM")]
    [InlineData("a@inbox.mailinator.com")]
    public void Disposable_domains_and_their_subdomains_are_flagged(string email)
    {
        _rule.Evaluate(Context(email: email))!.Points.Should().Be(50);
    }

    [Theory]
    [InlineData("cliente@loja.com")]
    [InlineData("maria.silva@gmail.com")]
    [InlineData("joao@teste.com")] // dominio de teste, sozinho, nao e suspeito
    [InlineData("fulano@fraude-contra-incendio.org.br")] // termo so no dominio nao conta
    [InlineData("a@notmailinator.com")] // sufixo de texto != subdominio
    [InlineData("a@mailinator.com.br")] // dominio diferente
    [InlineData("a@empresa.com.br")]
    public void Legitimate_addresses_are_not_flagged(string email)
    {
        _rule.Evaluate(Context(email: email)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sem-arroba")]
    [InlineData("@so-dominio.com")]
    public void Unparseable_addresses_never_throw_and_do_not_score(string email)
    {
        var act = () => _rule.Evaluate(Context(email: email));

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void Keyword_and_disposable_domain_together_still_count_once()
    {
        _rule.Evaluate(Context(email: "fraude@mailinator.com"))!.Points.Should().Be(50);
    }

    [Fact]
    public void Null_context_is_rejected()
    {
        var act = () => _rule.Evaluate(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
