using System.Net;
using System.Net.Http.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Ingestion.Application.Retention;
using static RiskGuard.IntegrationTests.Ingestion.IngestionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Security;

/// <summary>LGPD: o payload bruto (dados pessoais do comprador) tem prazo de vida e o lojista pode pedir a eliminacao de um comprador.</summary>
[Collection(InfrastructureCollection.Name)]
public class PersonalDataTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri Anonymize = new("/api/v1/privacy/customers/anonymize", UriKind.Relative);

    private ApiFactory Api => infrastructure.Api;

    private sealed record Posted(string ExternalId, Guid WebhookId);

    private async Task<Posted> PostOrderAsync(Guid tenantId, string email)
    {
        using var client = Api.CreateClient();
        var externalId = NewExternalId();
        var body = $$"""{ "id": {{externalId}}, "email": "{{email}}", "created_at": "2026-03-01T10:15:30Z", "total_price": "10.00" }""";
        var response = await WebhookTestClient.PostAsync(client, "shopify", tenantId, body);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var webhookId = (await response.Content.ReadFromJsonAsync<AcceptedBody>())!.WebhookId;
        await WaitForRiskEventAsync(Api, tenantId, externalId); // pedido processado, avaliado
        await WaitForAsync(infrastructure, webhookId, s => s.Status == "Processed");
        return new Posted(externalId, webhookId);
    }

    private async Task<T> Scalar<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private Task<string> PayloadOf(Guid webhookId) => Scalar<string>("SELECT \"Payload\"::text FROM ingestion.\"RawWebhooks\" WHERE \"Id\" = @id", ("id", webhookId));

    private Task<object?> PurgedAtOf(Guid webhookId) => Scalar<object?>("SELECT \"PayloadPurgedAt\" FROM ingestion.\"RawWebhooks\" WHERE \"Id\" = @id", ("id", webhookId));

    private Task AgeAsync(Guid webhookId, int days, string? status = null) =>
        ExecuteAsAdminAsync(
            infrastructure,
            "UPDATE ingestion.\"RawWebhooks\" SET \"ReceivedAt\" = now() - make_interval(days => @d), \"Status\" = COALESCE(@s, \"Status\") WHERE \"Id\" = @id",
            ("d", days), ("s", (object?)status ?? DBNull.Value), ("id", webhookId));

    private async Task RunRetentionAsync()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PurgeExpiredRawPayloadsJob>().RunAsync();
    }

    // ---- retencao ----

    [Fact]
    public async Task Old_finished_payloads_are_erased_across_all_tenants_but_pending_and_recent_ones_are_kept()
    {
        var (tenantA, _) = await RegisterAsync(Api);
        var (tenantB, _) = await RegisterAsync(Api);
        var oldProcessedA = await PostOrderAsync(tenantA, "comprador.a@example.com");
        var oldFailedB = await PostOrderAsync(tenantB, "comprador.b@example.com");
        var oldPendingA = await PostOrderAsync(tenantA, "pendente@example.com");
        var recentA = await PostOrderAsync(tenantA, "recente@example.com");
        await AgeAsync(oldProcessedA.WebhookId, 40);
        await AgeAsync(oldFailedB.WebhookId, 40, status: "Failed");
        await AgeAsync(oldPendingA.WebhookId, 40, status: "Received"); // ainda sera processado: nunca apagar
        await AgeAsync(recentA.WebhookId, 5);

        await RunRetentionAsync();

        (await PayloadOf(oldProcessedA.WebhookId)).Should().Be("{}");
        (await PayloadOf(oldFailedB.WebhookId)).Should().Be("{}");
        (await PurgedAtOf(oldProcessedA.WebhookId)).Should().NotBeNull();
        (await Scalar<string>("SELECT \"Headers\"::text FROM ingestion.\"RawWebhooks\" WHERE \"Id\" = @id", ("id", oldProcessedA.WebhookId))).Should().Be("{}");
        (await PayloadOf(oldPendingA.WebhookId)).Should().Contain("pendente@example.com");
        (await PurgedAtOf(oldPendingA.WebhookId)).Should().Be(DBNull.Value);
        (await PayloadOf(recentA.WebhookId)).Should().Contain("recente@example.com");

        // os pedidos normalizados e os metadados do webhook permanecem
        (await Scalar<long>("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"ExternalOrderId\" = @e", ("e", oldProcessedA.ExternalId))).Should().Be(1);
        (await Scalar<string>("SELECT \"Status\" FROM ingestion.\"RawWebhooks\" WHERE \"Id\" = @id", ("id", oldProcessedA.WebhookId))).Should().Be("Processed");
    }

    [Fact]
    public async Task The_purge_is_idempotent_and_does_not_touch_already_purged_rows()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        var old = await PostOrderAsync(tenantId, "idempotente@example.com");
        await AgeAsync(old.WebhookId, 40);
        await RunRetentionAsync();
        var firstPurgedAt = await PurgedAtOf(old.WebhookId);

        await RunRetentionAsync();

        (await PurgedAtOf(old.WebhookId)).Should().Be(firstPurgedAt);
    }

    [Fact]
    public async Task The_cleanup_is_scheduled_as_a_recurring_job_and_can_be_disabled()
    {
        await using var disabled = new ApiFactory(infrastructure, extraSettings: new Dictionary<string, string> { ["Retention:Enabled"] = "false" });
        _ = disabled.CreateClient();

        RecurringJobIds(Api).Should().Contain(("ingestion-purge-raw-payloads", "0 3 * * *"));
        RecurringJobIds(disabled).Should().NotContain(j => j.Id == "ingestion-purge-raw-payloads");
    }

    private static List<(string Id, string Cron)> RecurringJobIds(ApiFactory factory)
    {
        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        return Hangfire.Storage.StorageConnectionExtensions.GetRecurringJobs(connection).Select(j => (j.Id, j.Cron)).ToList();
    }

    // ---- anonimizacao ----

    [Fact]
    public async Task Requests_require_authentication_and_a_valid_email()
    {
        var (_, email) = await RegisterAsync(Api);
        using var anonymous = Api.CreateClient();
        using var client = await AuthenticatedClientAsync(Api, email);

        (await anonymous.PostAsJsonAsync(Anonymize, new { email = "a@b.com" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync(Anonymize, new { email = "nao-e-email" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(Anonymize, new { email = (string?)null })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anonymizing_a_buyer_erases_their_data_in_every_module_for_that_tenant_only()
    {
        var (tenantA, emailA) = await RegisterAsync(Api);
        var (tenantB, _) = await RegisterAsync(Api);
        const string victim = "a_b@example.com"; // '_' e curinga em LIKE: nao pode apagar o vizinho "axb@example.com"
        const string bystander = "axb@example.com";
        await SeedRefundsAsync(infrastructure, tenantA, victim, 2);
        await SeedRefundsAsync(infrastructure, tenantA, bystander, 1);
        await SeedRefundsAsync(infrastructure, tenantB, victim, 3);
        var victim1 = await PostOrderAsync(tenantA, victim);
        var victim2 = await PostOrderAsync(tenantA, "A_B@Example.COM"); // mesma pessoa, outra caixa
        var other = await PostOrderAsync(tenantA, bystander);
        var otherTenant = await PostOrderAsync(tenantB, victim);
        using var client = await AuthenticatedClientAsync(Api, emailA);
        var evaluationsBefore = await Scalar<long>("SELECT count(*) FROM risk.\"RiskEvaluations\" WHERE \"TenantId\" = @t", ("t", tenantA));

        var response = await client.PostAsJsonAsync(Anonymize, new { email = "A_B@EXAMPLE.com" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ingestion: pedidos e payloads
        (await Scalar<long>("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"TenantId\" = @t AND \"CustomerEmail\" = @e", ("t", tenantA), ("e", victim))).Should().Be(0);
        (await Scalar<long>("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"TenantId\" = @t AND \"CustomerEmail\" LIKE 'anonymized-%'", ("t", tenantA))).Should().Be(2);
        (await PayloadOf(victim1.WebhookId)).Should().Be("{}");
        (await PayloadOf(victim2.WebhookId)).Should().Be("{}");
        (await PurgedAtOf(victim1.WebhookId)).Should().NotBeNull();

        // o vizinho cujo e-mail so casaria se '_' fosse curinga nao foi tocado
        (await PayloadOf(other.WebhookId)).Should().Contain(bystander);
        (await Scalar<long>("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"ExternalOrderId\" = @e AND \"CustomerEmail\" = @m", ("e", other.ExternalId), ("m", bystander))).Should().Be(1);

        // risk: historico desvinculado; avaliacoes (sem e-mail) permanecem
        (await Scalar<long>("SELECT count(*) FROM risk.\"CustomerHistories\" WHERE \"TenantId\" = @t AND \"CustomerEmail\" = @e", ("t", tenantA), ("e", victim))).Should().Be(0);
        (await Scalar<long>("SELECT count(*) FROM risk.\"CustomerHistories\" WHERE \"TenantId\" = @t AND \"CustomerEmail\" = @e", ("t", tenantA), ("e", bystander))).Should().Be(1);
        (await Scalar<long>("SELECT count(*) FROM risk.\"RiskEvaluations\" WHERE \"TenantId\" = @t", ("t", tenantA))).Should().Be(evaluationsBefore);

        // outro lojista com o MESMO comprador: intocado (e responsabilidade dele)
        (await PayloadOf(otherTenant.WebhookId)).Should().Contain(victim);
        (await Scalar<long>("SELECT count(*) FROM ingestion.\"Orders\" WHERE \"TenantId\" = @t AND \"CustomerEmail\" = @e", ("t", tenantB), ("e", victim))).Should().Be(1);
        (await Scalar<long>("SELECT count(*) FROM risk.\"CustomerHistories\" WHERE \"TenantId\" = @t AND \"CustomerEmail\" = @e", ("t", tenantB), ("e", victim))).Should().Be(1);
    }

    [Fact]
    public async Task Anonymization_is_idempotent_and_the_token_cannot_be_linked_back()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        var posted = await PostOrderAsync(tenantId, "esquecer@example.com");
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PostAsJsonAsync(Anonymize, new { email = "esquecer@example.com" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = await Scalar<string>("SELECT \"CustomerEmail\" FROM ingestion.\"Orders\" WHERE \"ExternalOrderId\" = @e", ("e", posted.ExternalId));
        (await client.PostAsJsonAsync(Anonymize, new { email = "esquecer@example.com" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        token.Should().StartWith("anonymized-").And.NotContain("esquecer");
        (await Scalar<string>("SELECT \"CustomerEmail\" FROM ingestion.\"Orders\" WHERE \"ExternalOrderId\" = @e", ("e", posted.ExternalId))).Should().Be(token); // nada mudou na 2a chamada
    }

    [Fact]
    public void Like_wildcards_in_an_email_are_escaped()
    {
        AnonymizeCustomerIngestionHandler.EscapeLike("a_b%c\\d@x.com").Should().Be("a\\_b\\%c\\\\d@x.com");
    }

    private sealed record AcceptedBody(Guid WebhookId);
}
