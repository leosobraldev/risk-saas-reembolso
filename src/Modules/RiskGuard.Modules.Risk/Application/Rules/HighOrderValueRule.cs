namespace RiskGuard.Modules.Risk.Application.Rules;

/// <summary>Pedidos de valor alto: +10 a partir de 5.000 e +20 a partir de 10.000 (mesma moeda do pedido).</summary>
public sealed class HighOrderValueRule : IRiskRule
{
    public const decimal FirstTier = 5_000m;
    public const decimal SecondTier = 10_000m;

    public string Name => nameof(HighOrderValueRule);

    public RiskContribution? Evaluate(RiskContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.TotalAmount >= SecondTier)
        {
            return new RiskContribution(Name, $"Pedido de valor muito alto (>= {SecondTier:N0}).", 20);
        }

        return context.TotalAmount >= FirstTier
            ? new RiskContribution(Name, $"Pedido de valor alto (>= {FirstTier:N0}).", 10)
            : null;
    }
}
