using System.Net;
using System.Net.Http.Json;
using Hangfire;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.Core.Contracts;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Tenant.Application.Audit;
using RiskGuard.Modules.Tenant.Application.Settings;
using static RiskGuard.IntegrationTests.Actions.ActionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Tenant;

/// <summary>
/// Trilha de auditoria: toda mudanca sensivel (configuracao, conexoes, anonimizacao, liberacao de pedido, ciclo de vida do lojista)
/// deixa um evento com quem fez, o que e quando. A trilha e isolada por lojista, nao guarda segredos nem dados de compradores e
/// e imutavel ate no banco.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class AuditTrailTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri Settings = new("/api/v1/tenants/me/settings", UriKind.Relative);
    private static readonly Uri Events = new("/api/v1/audit-events", UriKind.Relative);

    private ApiFactory Api => infrastructure.Api;

    private sealed record EventBody(Guid Id, DateTimeOffset OccurredAt, string ActorId, string Action, string? Target, string? Details);

    private sealed record PageBody(List<EventBody> Items, int Page, int PageSize, int Total);

    private static async Task<List<EventBody>> EventsOfAsync(HttpClient client, string? query = null)
    {
        var page = await client.GetFromJsonAsync<PageBody>(new Uri($"/api/v1/audit-events?pageSize=100{query}", UriKind.Relative));
        return page!.Items;
    }

    private static string ActorOf(Guid tenantId) => $"auth0|{tenantId:N}"; // sub emitido pelo TestIdentity

    // ---- API ----

    [Fact]
    public async Task Reading_the_trail_requires_authentication_and_there_is_no_write_route()
    {
        using var anonymous = Api.CreateClient();
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await anonymous.GetAsync(Events)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, Events);
            ((int)(await client.SendAsync(request)).StatusCode).Should().BeOneOf(404, 405);
        }
    }

    [Fact]
    public async Task Registering_a_tenant_leaves_an_event_attributed_to_the_system()
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        var events = await EventsOfAsync(client);

        events.Should().Contain(e => e.Action == AuditActions.TenantRegistered && e.ActorId == "system");
    }

    [Fact]
    public async Task Changing_settings_records_who_changed_what_from_and_to()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PutAsJsonAsync(Settings, new { highRiskThreshold = 65, autoBlockEnabled = true })).EnsureSuccessStatusCode();

        var evt = (await EventsOfAsync(client, $"&action={AuditActions.SettingsUpdated}")).Single();
        evt.ActorId.Should().Be(ActorOf(tenantId));
        evt.Target.Should().Be("settings");
        evt.Details.Should().Be("limite 80 -> 65; bloqueio automatico desligado -> ligado");
        evt.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task A_rejected_change_leaves_no_trace_because_nothing_changed()
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PutAsJsonAsync(Settings, new { highRiskThreshold = 500, autoBlockEnabled = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await EventsOfAsync(client, $"&action={AuditActions.SettingsUpdated}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Connecting_and_removing_a_platform_is_recorded_without_any_secret()
    {
        var (tenantId, email) = await RegisterAsync(Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(Api, email);
        var credentials = TestCredentialStore.NewCredentials("shopify");

        (await client.PutAsJsonAsync(
            new Uri("/api/v1/tenants/me/connections/shopify", UriKind.Relative),
            new { accountId = credentials.AccountId, webhookSecret = credentials.WebhookSecret, accessToken = credentials.AccessToken })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(
            new Uri("/api/v1/tenants/me/connections/shopify", UriKind.Relative),
            new { accountId = credentials.AccountId, webhookSecret = credentials.WebhookSecret })).EnsureSuccessStatusCode();
        (await client.DeleteAsync(new Uri("/api/v1/tenants/me/connections/shopify", UriKind.Relative))).EnsureSuccessStatusCode();

        var events = (await EventsOfAsync(client)).Where(e => e.Target == "shopify").OrderBy(e => e.OccurredAt).ToList();
        events.Select(e => e.Action).Should().Equal(AuditActions.ConnectionUpserted, AuditActions.ConnectionUpserted, AuditActions.ConnectionRemoved);
        events[0].Details.Should().Be($"criada; loja {credentials.AccountId}; token de acesso informado");
        events[1].Details.Should().Be($"substituida; loja {credentials.AccountId}; token de acesso ausente");
        events.Should().OnlyContain(e => e.ActorId == ActorOf(tenantId));

        var everything = string.Join('|', events.Select(e => $"{e.Target}{e.Details}"));
        everything.Should().NotContain(credentials.WebhookSecret).And.NotContain(credentials.AccessToken!);
    }

    [Fact]
    public async Task A_failed_connection_attempt_leaves_no_trace()
    {
        var (_, email) = await RegisterAsync(Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PutAsJsonAsync(
            new Uri("/api/v1/tenants/me/connections/shopify", UriKind.Relative),
            new { accountId = "nao-e-um-dominio", webhookSecret = "x" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await EventsOfAsync(client, $"&action={AuditActions.ConnectionUpserted}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Requesting_a_customer_anonymization_is_recorded_without_the_email()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);
        const string Victim = "esquecer-auditoria@example.com";

        (await client.PostAsJsonAsync(new Uri("/api/v1/privacy/customers/anonymize", UriKind.Relative), new { email = Victim })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var events = await EventsOfAsync(client, $"&action={AuditActions.CustomerAnonymizationRequested}");
        events.Should().ContainSingle().Which.ActorId.Should().Be(ActorOf(tenantId));
        (await Scalar<long>("SELECT count(*) FROM tenant.\"AuditEvents\" WHERE \"Details\" ILIKE @e OR \"Target\" ILIKE @e", ("e", "%esquecer-auditoria%"))).Should().Be(0);
    }

    [Fact]
    public async Task An_invalid_anonymization_request_leaves_no_trace()
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PostAsJsonAsync(new Uri("/api/v1/privacy/customers/anonymize", UriKind.Relative), new { email = "invalido" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        (await EventsOfAsync(client, $"&action={AuditActions.CustomerAnonymizationRequested}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Releasing_a_blocked_order_is_recorded_with_the_action_and_the_order()
    {
        var (tenantId, email) = await RegisterAsync(Api);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true, threshold: 40);
        using var anonymous = Api.CreateClient();
        var orderId = await PostShopifyOrderAsync(anonymous, tenantId, "fraude@teste.com");
        var action = await WaitForStatusAsync(infrastructure, tenantId, orderId, "Succeeded");
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PostAsync(new Uri($"/api/v1/actions/{action.Id}/release", UriKind.Relative), content: null)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var evt = (await EventsOfAsync(client, $"&action={AuditActions.ActionReleaseRequested}")).Single();
        evt.Target.Should().Be(action.Id.ToString());
        evt.Details.Should().Be($"shopify; pedido {orderId}");
        evt.ActorId.Should().Be(ActorOf(tenantId));
    }

    [Fact]
    public async Task A_release_the_tenant_cannot_perform_is_not_recorded()
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);

        (await client.PostAsync(new Uri($"/api/v1/actions/{Guid.NewGuid()}/release", UriKind.Relative), content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await EventsOfAsync(client, $"&action={AuditActions.ActionReleaseRequested}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Deactivating_a_tenant_is_recorded_as_a_system_operation()
    {
        var (tenantId, _) = await RegisterAsync(Api);

        await using (var scope = Api.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DeactivateTenantCommand(tenantId))).IsSuccess.Should().BeTrue();
        }

        (await Scalar<string>("SELECT \"ActorId\" FROM tenant.\"AuditEvents\" WHERE \"TenantId\" = @t AND \"Action\" = @a", ("t", tenantId), ("a", AuditActions.TenantDeactivated)))
            .Should().Be("system");
    }

    [Fact]
    public async Task The_trail_is_paged_newest_first_filterable_and_validated()
    {
        var (_, email) = await RegisterAsync(Api);
        using var client = await AuthenticatedClientAsync(Api, email);
        for (var i = 0; i < 3; i++)
        {
            (await client.PutAsJsonAsync(Settings, new { highRiskThreshold = 50 + i, autoBlockEnabled = false })).EnsureSuccessStatusCode();
        }

        var first = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/audit-events?page=1&pageSize=2", UriKind.Relative));
        var second = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/audit-events?page=2&pageSize=2", UriKind.Relative));

        first!.Total.Should().BeGreaterThanOrEqualTo(4); // cadastro + 3 mudancas (+ conexoes)
        first.Items.Should().HaveCount(2).And.BeInDescendingOrder(e => e.OccurredAt);
        first.Items.Select(e => e.Id).Should().NotIntersectWith(second!.Items.Select(e => e.Id));
        (await client.GetAsync(new Uri("/api/v1/audit-events?page=0", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/audit-events?pageSize=5000", UriKind.Relative)))!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task One_tenant_never_sees_the_trail_of_another()
    {
        var (_, emailA) = await RegisterAsync(Api);
        var (idB, emailB) = await RegisterAsync(Api);
        using var a = await AuthenticatedClientAsync(Api, emailA);
        using var b = await AuthenticatedClientAsync(Api, emailB);
        (await b.PutAsJsonAsync(Settings, new { highRiskThreshold = 33, autoBlockEnabled = true })).EnsureSuccessStatusCode();

        var seenByA = await EventsOfAsync(a);
        var seenByB = await EventsOfAsync(b);

        seenByA.Should().NotContain(e => e.Action == AuditActions.SettingsUpdated); // A nunca mudou nada; a mudanca de B nao aparece para A
        seenByA.Should().NotContain(e => e.ActorId == ActorOf(idB));
        seenByB.Should().Contain(e => e.Action == AuditActions.SettingsUpdated && e.ActorId == ActorOf(idB));
    }

    // ---- banco: imutabilidade e isolamento ----

    [Fact]
    public async Task The_application_role_can_read_and_append_but_never_rewrite_history()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        var id = await InsertAsync(tenantId, daysAgo: 1);

        (await TryAsAppAsync(tenantId, "UPDATE tenant.\"AuditEvents\" SET \"Details\" = 'adulterado'")).Should().Be("42501");
        (await TryAsAppAsync(tenantId, "TRUNCATE tenant.\"AuditEvents\"")).Should().Be("42501");
        (await Scalar<long>("SELECT count(*) FROM tenant.\"AuditEvents\" WHERE \"Id\" = @i AND \"Details\" IS NULL", ("i", id))).Should().Be(1);
    }

    [Fact]
    public async Task Recent_events_cannot_be_deleted_by_the_application_role_even_by_mistake()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        var recent = await InsertAsync(tenantId, daysAgo: 10);
        var withinMinimum = await InsertAsync(tenantId, daysAgo: 179);

        var deleted = await DeleteAsAppAsync(tenantId, "DELETE FROM tenant.\"AuditEvents\"");

        deleted.Should().Be(0); // a RLS so deixa apagar o que passou de 180 dias; este lojista nao tem nada tao velho
        (await Scalar<long>("SELECT count(*) FROM tenant.\"AuditEvents\" WHERE \"Id\" = ANY(@ids)", ("ids", new[] { recent, withinMinimum }))).Should().Be(2);
    }

    [Fact]
    public async Task Without_a_tenant_in_the_session_the_role_sees_and_writes_nothing()
    {
        var (tenantId, _) = await RegisterAsync(Api);

        await using var connection = new NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM tenant.\"AuditEvents\"", connection);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(0);

        await using var insert = new NpgsqlCommand(
            "INSERT INTO tenant.\"AuditEvents\" (\"Id\", \"TenantId\", \"OccurredAt\", \"ActorId\", \"Action\") VALUES (gen_random_uuid(), @t, now(), 'x', 'y')", connection);
        insert.Parameters.AddWithValue("t", tenantId);
        var act = async () => await insert.ExecuteNonQueryAsync();
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task A_tenant_session_cannot_write_an_event_for_another_tenant()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        var (otherId, _) = await RegisterAsync(Api);

        var state = await TryAsAppAsync(
            tenantId,
            $"INSERT INTO tenant.\"AuditEvents\" (\"Id\", \"TenantId\", \"OccurredAt\", \"ActorId\", \"Action\") VALUES (gen_random_uuid(), '{otherId}', now(), 'x', 'y')");

        state.Should().Be("42501");
    }

    // ---- retencao ----

    [Fact]
    public async Task The_retention_job_erases_only_events_older_than_the_configured_period()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        var old = await InsertAsync(tenantId, daysAgo: 400);
        var middle = await InsertAsync(tenantId, daysAgo: 200); // mais velho que o minimo, mais novo que AuditDays (365)
        var recent = await InsertAsync(tenantId, daysAgo: 5);

        await RunRetentionAsync();

        (await ExistsAsync(old)).Should().BeFalse();
        (await ExistsAsync(middle)).Should().BeTrue();
        (await ExistsAsync(recent)).Should().BeTrue();
    }

    [Fact]
    public async Task The_database_blocks_an_over_eager_purge_of_recent_events()
    {
        var (tenantId, _) = await RegisterAsync(Api);
        var recent = await InsertAsync(tenantId, daysAgo: 30);

        await using var scope = Api.Services.CreateAsyncScope();
        var purged = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PurgeExpiredAuditEventsCommand(tenantId, DateTimeOffset.UtcNow.AddDays(1)));

        purged.Should().Be(0); // mesmo com corte "amanha", a RLS so deixa apagar o que passou de 180 dias
        (await ExistsAsync(recent)).Should().BeTrue();
    }

    [Fact]
    public void The_retention_job_is_scheduled_daily()
    {
        RecurringJobIds(Api).Should().Contain(("tenant-purge-audit-events", "0 3 * * *"));
    }

    private static List<(string Id, string Cron)> RecurringJobIds(ApiFactory factory)
    {
        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        return Hangfire.Storage.StorageConnectionExtensions.GetRecurringJobs(connection).Select(j => (j.Id, j.Cron)).ToList();
    }

    // ---- helpers ----

    private async Task RunRetentionAsync()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PurgeExpiredAuditEventsJob>().RunAsync();
    }

    private async Task<Guid> InsertAsync(Guid tenantId, int daysAgo)
    {
        var id = Guid.NewGuid();
        await ExecuteAsAdminAsync(
            infrastructure,
            "INSERT INTO tenant.\"AuditEvents\" (\"Id\", \"TenantId\", \"OccurredAt\", \"ActorId\", \"Action\") VALUES (@i, @t, now() - make_interval(days => @d), 'teste', 'teste.evento')",
            ("i", id), ("t", tenantId), ("d", daysAgo));
        return id;
    }

    private async Task<bool> ExistsAsync(Guid id) =>
        await Scalar<long>("SELECT count(*) FROM tenant.\"AuditEvents\" WHERE \"Id\" = @i", ("i", id)) == 1;

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

    /// <summary>Executa como o papel da aplicacao, com o tenant na sessao (como a API faz).</summary>
    private async Task<string?> TryAsAppAsync(Guid tenantId, string sql)
    {
        await using var connection = await OpenAppAsync(tenantId);
        await using var command = new NpgsqlCommand(sql, connection);
        try
        {
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    private async Task<int> DeleteAsAppAsync(Guid tenantId, string sql)
    {
        await using var connection = await OpenAppAsync(tenantId);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenAppAsync(Guid tenantId)
    {
        var connection = new NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();
        await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", connection);
        set.Parameters.AddWithValue("t", tenantId.ToString());
        await set.ExecuteNonQueryAsync();
        return connection;
    }
}
