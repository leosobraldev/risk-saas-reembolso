using RiskGuard.Modules.Tenant.Domain;

namespace RiskGuard.UnitTests.Tenant;

public class AuditEventTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Records_who_did_what_and_when()
    {
        var evt = AuditEvent.Create(TenantId, "auth0|abc", "settings.updated", "settings", "limite 80 -> 70", Now);

        evt.TenantId.Should().Be(TenantId);
        evt.ActorId.Should().Be("auth0|abc");
        evt.Action.Should().Be("settings.updated");
        evt.Target.Should().Be("settings");
        evt.Details.Should().Be("limite 80 -> 70");
        evt.OccurredAt.Should().Be(Now);
        evt.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_an_actor_the_event_is_attributed_to_the_system(string? actor)
    {
        AuditEvent.Create(TenantId, actor, "tenant.registered", null, null, Now).ActorId.Should().Be(AuditEvent.SystemActor);
    }

    [Fact]
    public void Details_longer_than_the_limit_are_truncated_instead_of_failing_the_operation()
    {
        var evt = AuditEvent.Create(TenantId, null, "settings.updated", null, new string('x', 5000), Now);

        evt.Details!.Length.Should().Be(AuditEvent.DetailsMaxLength);
    }

    [Fact]
    public void Blank_target_and_details_become_null()
    {
        var evt = AuditEvent.Create(TenantId, null, "settings.updated", "  ", "", Now);

        evt.Target.Should().BeNull();
        evt.Details.Should().BeNull();
    }

    [Fact]
    public void An_event_needs_a_tenant_and_an_action()
    {
        var noTenant = () => AuditEvent.Create(Guid.Empty, null, "settings.updated", null, null, Now);
        var noAction = () => AuditEvent.Create(TenantId, null, " ", null, null, Now);

        noTenant.Should().Throw<ArgumentException>();
        noAction.Should().Throw<ArgumentException>();
    }
}
