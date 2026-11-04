namespace RiskGuard.Modules.Risk.Application.Rules;

/// <summary>+10 pontos por reembolso anterior do cliente, ate o teto de 40.</summary>
public sealed class RefundHistoryRule : IRiskRule
{
    public const int PointsPerRefund = 10;
    public const int MaxPoints = 40;

    public string Name => nameof(RefundHistoryRule);

    public RiskContribution? Evaluate(RiskContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.PreviousRefunds <= 0)
        {
            return null;
        }

        // long evita overflow com contagens enormes
        var points = (int)Math.Min(MaxPoints, (long)context.PreviousRefunds * PointsPerRefund);
        return new RiskContribution(Name, $"Cliente com {context.PreviousRefunds} reembolso(s) anterior(es).", points);
    }
}
