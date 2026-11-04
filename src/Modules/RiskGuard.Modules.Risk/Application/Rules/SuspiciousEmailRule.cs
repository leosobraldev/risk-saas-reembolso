using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Risk.Application.Rules;

/// <summary>+50 pontos para e-mails com termos de fraude na parte local ou de dominios descartaveis.</summary>
public sealed class SuspiciousEmailRule : IRiskRule
{
    public const int Points = 50;

    private static readonly string[] SuspiciousTerms = ["fraude", "fraud", "golpe", "golpista", "scam"];

    private static readonly string[] DisposableDomains =
    [
        "mailinator.com",
        "guerrillamail.com",
        "10minutemail.com",
        "tempmail.com",
        "yopmail.com",
        "trashmail.com",
    ];

    public string Name => nameof(SuspiciousEmailRule);

    public RiskContribution? Evaluate(RiskContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var email = EmailRules.Normalize(context.CustomerEmail);
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1)
        {
            return null;
        }

        var local = email[..at];
        var domain = email[(at + 1)..];

        var term = SuspiciousTerms.FirstOrDefault(t => local.Contains(t, StringComparison.Ordinal));
        if (term is not null)
        {
            return new RiskContribution(Name, $"E-mail suspeito: o endereco contem o termo '{term}'.", Points);
        }

        var disposable = DisposableDomains.FirstOrDefault(d =>
            domain.Equals(d, StringComparison.Ordinal) || domain.EndsWith("." + d, StringComparison.Ordinal));
        return disposable is null
            ? null
            : new RiskContribution(Name, $"E-mail de dominio descartavel ({disposable}).", Points);
    }
}
