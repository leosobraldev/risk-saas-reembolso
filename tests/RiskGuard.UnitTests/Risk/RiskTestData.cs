using RiskGuard.Modules.Risk.Application.Rules;

namespace RiskGuard.UnitTests.Risk;

internal static class RiskTestData
{
    public static RiskContext Context(
        string email = "cliente@loja.com",
        decimal total = 100m,
        int refunds = 0) =>
        new(
            TenantId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            Platform: "shopify",
            ExternalOrderId: "1001",
            CustomerEmail: email,
            TotalAmount: total,
            OrderCreatedAt: new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            PreviousRefunds: refunds);
}
