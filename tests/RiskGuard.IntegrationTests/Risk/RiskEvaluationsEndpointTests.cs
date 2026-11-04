using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Risk;

/// <summary>GET /api/v1/risk/evaluations (SDD 8): lista pedidos e scores do lojista autenticado.</summary>
[Collection(InfrastructureCollection.Name)]
public class RiskEvaluationsEndpointTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri Route = new("/api/v1/risk/evaluations", UriKind.Relative);

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = infrastructure.Api.CreateClient();

        (await client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lists_only_the_callers_evaluations_with_their_factors_newest_first()
    {
        var (tenantA, emailA) = await RegisterAsync(infrastructure.Api);
        var (tenantB, _) = await RegisterAsync(infrastructure.Api);
        using var anonymous = infrastructure.Api.CreateClient();
        var first = await PostShopifyOrderAsync(anonymous, tenantA, "maria@gmail.com");
        await WaitForRiskEventAsync(infrastructure.Api, tenantA, first);
        var second = await PostShopifyOrderAsync(anonymous, tenantA, "fraude@teste.com");
        await WaitForRiskEventAsync(infrastructure.Api, tenantA, second);
        var other = await PostShopifyOrderAsync(anonymous, tenantB, "fraude@teste.com");
        await WaitForRiskEventAsync(infrastructure.Api, tenantB, other);

        using var client = await AuthenticatedAsync(emailA);
        var page = await client.GetFromJsonAsync<PageBody>(Route);

        page!.Total.Should().Be(2);
        page.Items.Select(i => i.ExternalOrderId).Should().Equal(second, first); // mais recente primeiro
        page.Items[0].Score.Should().Be(50);
        page.Items[0].Level.Should().Be("Medium");
        page.Items[0].Factors.Should().ContainSingle().Which.PointsAdded.Should().Be(50);
        page.Items[1].Factors.Should().BeEmpty();
        page.Items.Should().NotContain(i => i.ExternalOrderId == other);
    }

    [Fact]
    public async Task Supports_pagination_and_level_filter()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api);
        using var anonymous = infrastructure.Api.CreateClient();
        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var id = await PostShopifyOrderAsync(anonymous, tenantId, i == 2 ? "fraude@teste.com" : "ok@gmail.com");
            await WaitForRiskEventAsync(infrastructure.Api, tenantId, id);
            ids.Add(id);
        }

        using var client = await AuthenticatedAsync(email);

        var firstPage = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/risk/evaluations?page=1&pageSize=2", UriKind.Relative));
        var secondPage = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/risk/evaluations?page=2&pageSize=2", UriKind.Relative));
        var medium = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/risk/evaluations?level=Medium", UriKind.Relative));

        firstPage!.Items.Should().HaveCount(2);
        firstPage.Total.Should().Be(3);
        secondPage!.Items.Should().HaveCount(1);
        medium!.Items.Should().ContainSingle().Which.ExternalOrderId.Should().Be(ids[2]);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("level=Catastrophic")]
    public async Task Invalid_query_parameters_return_400(string query)
    {
        var (_, email) = await RegisterAsync(infrastructure.Api);
        using var client = await AuthenticatedAsync(email);

        (await client.GetAsync(new Uri($"/api/v1/risk/evaluations?{query}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Page_size_is_capped_at_one_hundred()
    {
        var (_, email) = await RegisterAsync(infrastructure.Api);
        using var client = await AuthenticatedAsync(email);

        var page = await client.GetFromJsonAsync<PageBody>(new Uri("/api/v1/risk/evaluations?pageSize=100000", UriKind.Relative));

        page!.PageSize.Should().Be(100);
    }

    private async Task<HttpClient> AuthenticatedAsync(string email)
    {
        using var anonymous = infrastructure.Api.CreateClient();
        var client = infrastructure.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessTokenFor(email));
        return client;
    }

    private sealed record PageBody(List<ItemBody> Items, int Page, int PageSize, int Total);

    private sealed record ItemBody(Guid Id, Guid OrderId, string Platform, string ExternalOrderId, decimal TotalAmount, int Score, string Level, DateTimeOffset CalculatedAt, List<FactorBody> Factors);

    private sealed record FactorBody(string Reason, int PointsAdded);
}
