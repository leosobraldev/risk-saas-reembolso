using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Contracts;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Risk;

/// <summary>Criterio de aceite 2 e 3 (SDD 6), ponta a ponta: webhook -> Hangfire -> pedido -> RiskModule -> RiskCalculatedEvent.</summary>
[Collection(InfrastructureCollection.Name)]
public class RiskEvaluationFlowTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Acceptance_scenario_suspicious_email_scores_50_persists_factors_and_publishes_the_event()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        var evt = await WaitForRiskEventAsync(infrastructure.Api, tenantId, externalId);

        evt.Score.Should().Be(50);
        evt.Level.Should().Be(RiskLevel.Medium);
        evt.Platform.Should().Be("shopify");
        evt.OrderId.Should().NotBeEmpty();
        evt.EvaluationId.Should().NotBeEmpty();

        (await CountAsync(infrastructure, "SELECT count(*) FROM risk.\"RiskEvaluations\" WHERE \"TenantId\" = @t AND \"OrderId\" = @o AND \"Score\" = 50", ("t", tenantId), ("o", evt.OrderId)))
            .Should().Be(1);
        (await CountAsync(infrastructure, "SELECT count(*) FROM risk.\"RiskFactors\" WHERE \"TenantId\" = @t AND \"RiskEvaluationId\" = @e AND \"PointsAdded\" = 50", ("t", tenantId), ("e", evt.EvaluationId)))
            .Should().Be(1);
    }

    [Fact]
    public async Task Clean_customer_scores_zero_and_is_low_risk()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "maria@gmail.com", "89.90");
        var evt = await WaitForRiskEventAsync(infrastructure.Api, tenantId, externalId);

        evt.Score.Should().Be(0);
        evt.Level.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public async Task Customer_history_and_order_value_rules_combine_with_the_email_rule_to_high_risk()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        await SeedRefundsAsync(infrastructure, tenantId, "fraude@teste.com", 4); // +40
        using var client = infrastructure.Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com"); // +50 => 90
        var evt = await WaitForRiskEventAsync(infrastructure.Api, tenantId, externalId);

        evt.Score.Should().Be(90); // entrada do cenario 3 (score 90 > limite 80)
        evt.Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public async Task Refund_history_of_another_tenant_never_leaks_into_the_score()
    {
        var (tenantA, _) = await RegisterAsync(infrastructure.Api);
        var (tenantB, _) = await RegisterAsync(infrastructure.Api);
        await SeedRefundsAsync(infrastructure, tenantA, "cliente@x.com", 5); // historico so do tenant A
        using var client = infrastructure.Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantB, "cliente@x.com");
        var evt = await WaitForRiskEventAsync(infrastructure.Api, tenantB, externalId);

        evt.Score.Should().Be(0);
    }

    [Fact]
    public async Task Tenant_threshold_decides_the_level_of_the_same_score()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        await ExecuteAsAdminAsync(
            infrastructure,
            "UPDATE tenant.\"TenantSettings\" SET \"HighRiskThreshold\" = 40 WHERE \"TenantId\" = @t",
            ("t", tenantId)); // antes do primeiro webhook: o snapshot ainda nao esta em cache
        using var client = infrastructure.Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com"); // 50 > 40
        var evt = await WaitForRiskEventAsync(infrastructure.Api, tenantId, externalId);

        evt.Score.Should().Be(50);
        evt.Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public async Task Order_value_rule_scores_expensive_orders()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();

        var externalId = await PostShopifyOrderAsync(client, tenantId, "rico@gmail.com", "12000.00");
        var evt = await WaitForRiskEventAsync(infrastructure.Api, tenantId, externalId);

        evt.Score.Should().Be(20);
        evt.Level.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public async Task Redelivered_order_keeps_a_single_evaluation_and_republishes_the_same_result()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        var first = await WaitForRiskEventAsync(infrastructure.Api, tenantId, externalId);

        // mesma entrega de novo: mesmo id externo
        var redelivery = $$"""{ "id": {{externalId}}, "email": "fraude@teste.com", "created_at": "2026-03-01T10:15:30Z", "total_price": "100.00" }""";
        (await WebhookTestClient.PostAsync(client, "shopify", tenantId, redelivery)).EnsureSuccessStatusCode();

        var capture = infrastructure.Api.Services.GetRequiredService<RiskCalculatedCapture>();
        await WaitUntilAsync(() => capture.Events.Count(e => e.TenantId == tenantId && e.ExternalOrderId == externalId) >= 2);

        (await CountAsync(infrastructure, "SELECT count(*) FROM risk.\"RiskEvaluations\" WHERE \"TenantId\" = @t AND \"OrderId\" = @o", ("t", tenantId), ("o", first.OrderId)))
            .Should().Be(1);
        capture.Events.Where(e => e.TenantId == tenantId && e.ExternalOrderId == externalId)
            .Select(e => e.EvaluationId).Distinct().Should().ContainSingle().Which.Should().Be(first.EvaluationId);
    }

    [Fact]
    public async Task Risk_tables_enforce_row_level_security_for_the_application_role()
    {
        var (tenantId, _) = await RegisterAsync(infrastructure.Api);
        using var client = infrastructure.Api.CreateClient();
        var externalId = await PostShopifyOrderAsync(client, tenantId, "fraude@teste.com");
        await WaitForRiskEventAsync(infrastructure.Api, tenantId, externalId);

        await using var connection = new Npgsql.NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();
        foreach (var table in new[] { "RiskEvaluations", "RiskFactors", "CustomerHistories" })
        {
            await using var command = new Npgsql.NpgsqlCommand($"SELECT count(*) FROM risk.\"{table}\"", connection);
            ((long)(await command.ExecuteScalarAsync())!).Should().Be(0, $"sem tenant na sessao nada de risk.{table} e visivel");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException("Condicao nao atendida no prazo.");
    }
}
