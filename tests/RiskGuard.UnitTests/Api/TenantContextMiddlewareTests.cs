using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RiskGuard.Api.Middleware;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.UnitTests.Api;

public class TenantContextMiddlewareTests
{
    private readonly ITenantContextSetter _setter = Substitute.For<ITenantContextSetter>();

    [Fact]
    public async Task Authenticated_user_with_tenant_claim_sets_the_tenant()
    {
        var tenantId = Guid.NewGuid();

        var nextCalled = await Run(Principal(authenticated: true, (RiskGuardClaims.TenantId, tenantId.ToString())));

        nextCalled.Should().BeTrue();
        _setter.Received(1).SetTenant(tenantId);
    }

    [Theory]
    [InlineData(false, "00000000-0000-0000-0000-000000000001")]
    [InlineData(true, "not-a-guid")]
    [InlineData(true, "00000000-0000-0000-0000-000000000000")]
    [InlineData(true, null)]
    public async Task Anonymous_or_invalid_claim_leaves_the_scope_without_tenant(bool authenticated, string? claim)
    {
        var claims = claim is null ? [] : new[] { (RiskGuardClaims.TenantId, claim) };

        var nextCalled = await Run(Principal(authenticated, claims));

        nextCalled.Should().BeTrue();
        _setter.DidNotReceiveWithAnyArgs().SetTenant(default);
    }

    private async Task<bool> Run(ClaimsPrincipal user)
    {
        var called = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext { User = user }, _setter);
        return called;
    }

    private static ClaimsPrincipal Principal(bool authenticated, params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticated ? "test" : null));
}
