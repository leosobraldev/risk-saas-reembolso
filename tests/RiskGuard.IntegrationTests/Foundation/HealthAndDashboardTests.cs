using System.Net;
using RiskGuard.IntegrationTests.Infrastructure;

namespace RiskGuard.IntegrationTests.Foundation;

[Collection(InfrastructureCollection.Name)]
public class HealthAndDashboardTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Ready_endpoint_is_healthy_when_postgres_and_redis_are_up()
    {
        await using var factory = new ApiFactory(infrastructure);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hangfire_dashboard_is_available_in_development()
    {
        await using var factory = new ApiFactory(infrastructure, "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/hangfire", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hangfire_dashboard_is_denied_outside_development()
    {
        await using var factory = new ApiFactory(infrastructure, "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/hangfire", UriKind.Relative));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
