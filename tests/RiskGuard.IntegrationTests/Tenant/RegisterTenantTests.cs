using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.Core.Abstractions;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Tenant.Application.Commands;
using RiskGuard.Modules.Tenant.Application.Settings;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Tenant;

/// <summary>Cadastro de lojista (RF01): cria o lojista e o usuario dono no provedor de identidade, com compensacao se algo falhar no meio.</summary>
[Collection(InfrastructureCollection.Name)]
public class RegisterTenantTests(InfrastructureFixture infrastructure)
{
    private static async Task<Result<RegisteredTenant>> Register(ApiFactory factory, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RegisterTenantCommand(name, email));
    }

    private async Task<long> Count(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static FakeIdentityProvider Idp(ApiFactory factory) => factory.Services.GetRequiredService<FakeIdentityProvider>();

    [Fact]
    public async Task Registration_creates_tenant_default_settings_owner_link_and_the_identity_user_with_the_tenant_in_metadata()
    {
        var email = UniqueEmail();

        var result = await Register(infrastructure.Api, "  Minha Loja ", email.ToUpperInvariant());

        result.IsSuccess.Should().BeTrue();
        var registered = result.Value;
        registered.PasswordSetupUrl!.Scheme.Should().Be("https");

        var user = Idp(infrastructure.Api).FindByEmail(email)!;
        user.TenantId.Should().Be(registered.TenantId); // o provedor guarda o tenant: e ele que vira o claim no token
        user.ExternalId.Should().Be(registered.ExternalUserId);

        (await Count("SELECT count(*) FROM tenant.\"Tenants\" WHERE \"Id\" = @id AND \"Name\" = 'Minha Loja' AND \"Email\" = @e AND \"IsActive\"", ("id", registered.TenantId), ("e", email))).Should().Be(1);
        (await Count("SELECT count(*) FROM tenant.\"TenantSettings\" WHERE \"TenantId\" = @id AND \"HighRiskThreshold\" = 80 AND NOT \"AutoBlockEnabled\"", ("id", registered.TenantId))).Should().Be(1);
        (await Count("SELECT count(*) FROM tenant.\"TenantUsers\" WHERE \"TenantId\" = @id AND \"ExternalUserId\" = @u AND \"Role\" = 'Owner'", ("id", registered.TenantId), ("u", registered.ExternalUserId))).Should().Be(1);
    }

    [Fact]
    public async Task The_platform_stores_no_password_material_at_all()
    {
        (await Count("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'tenant' AND column_name ILIKE '%password%'")).Should().Be(0);
    }

    [Fact]
    public async Task A_duplicate_email_is_rejected_regardless_of_case_without_touching_the_identity_provider()
    {
        var (_, email) = await RegisterAsync(infrastructure.Api);
        var usersBefore = Idp(infrastructure.Api).Users.Count;

        var result = await Register(infrastructure.Api, "Outra", email.ToUpperInvariant());

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        Idp(infrastructure.Api).Users.Count.Should().Be(usersBefore);
    }

    [Theory]
    [InlineData("", "a@b.com")]
    [InlineData("Loja", "not-an-email")]
    [InlineData("Loja", "")]
    public async Task Invalid_input_is_rejected_before_calling_the_identity_provider(string name, string email)
    {
        var usersBefore = Idp(infrastructure.Api).Users.Count;

        var result = await Register(infrastructure.Api, name, email);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Validation);
        Idp(infrastructure.Api).Users.Count.Should().Be(usersBefore);
    }

    [Fact]
    public async Task When_the_identity_provider_is_unavailable_nothing_is_persisted()
    {
        await using var factory = new ApiFactory(infrastructure);
        var email = UniqueEmail();
        Idp(factory).FailNextCreate(IdentityErrors.Unavailable("fora do ar"));

        var result = await Register(factory, "Loja", email);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        (await Count("SELECT count(*) FROM tenant.\"Tenants\" WHERE \"Email\" = @e", ("e", email))).Should().Be(0);
    }

    [Fact]
    public async Task A_user_that_already_exists_in_the_provider_is_reported_as_a_conflict_and_nothing_is_persisted()
    {
        await using var factory = new ApiFactory(infrastructure);
        var email = UniqueEmail();
        Idp(factory).FailNextCreate(IdentityErrors.UserAlreadyExists);

        var result = await Register(factory, "Loja", email);

        result.Error.Code.Should().Be("identity.user_already_exists");
        (await Count("SELECT count(*) FROM tenant.\"Tenants\" WHERE \"Email\" = @e", ("e", email))).Should().Be(0);
    }

    [Fact]
    public async Task A_database_failure_after_the_provider_succeeded_removes_the_identity_user_compensation()
    {
        await using var factory = new ApiFactory(infrastructure);
        var (_, takenEmail) = await RegisterAsync(factory);
        var existingUser = Idp(factory).FindByEmail(takenEmail)!;
        var email = UniqueEmail();
        Idp(factory).NextUserId = existingUser.ExternalId; // forca violacao de unicidade em TenantUsers DEPOIS de o provedor criar o usuario

        var result = await Register(factory, "Loja", email);

        result.IsSuccess.Should().BeFalse();
        (await Count("SELECT count(*) FROM tenant.\"Tenants\" WHERE \"Email\" = @e", ("e", email))).Should().Be(0);
        Idp(factory).DeletedUserIds.Should().Contain(existingUser.ExternalId); // o usuario recem-criado no provedor foi removido
    }

    [Fact]
    public async Task A_failed_compensation_does_not_hide_the_original_error()
    {
        await using var factory = new ApiFactory(infrastructure);
        var (_, takenEmail) = await RegisterAsync(factory);
        Idp(factory).NextUserId = Idp(factory).FindByEmail(takenEmail)!.ExternalId;
        Idp(factory).FailNextDelete(IdentityErrors.Unavailable("delete falhou"));

        var result = await Register(factory, "Loja", UniqueEmail());

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Deactivation_blocks_the_owners_login_at_the_provider_and_survives_a_provider_failure()
    {
        await using var factory = new ApiFactory(infrastructure);
        var (tenantId, email) = await RegisterAsync(factory);
        var (otherTenant, otherEmail) = await RegisterAsync(factory);
        var user = Idp(factory).FindByEmail(email)!;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DeactivateTenantCommand(tenantId))).IsSuccess.Should().BeTrue();
        }

        user.Blocked.Should().BeTrue();
        Idp(factory).FindByEmail(otherEmail)!.Blocked.Should().BeFalse(); // so o dono do lojista desativado
        (await Count("SELECT count(*) FROM tenant.\"Tenants\" WHERE \"Id\" = @id AND NOT \"IsActive\"", ("id", tenantId))).Should().Be(1);

        // falha do provedor: a desativacao continua valendo (a API ja recusa os tokens), so o bloqueio no provedor fica pendente
        Idp(factory).FailNextBlock(IdentityErrors.Unavailable("fora do ar"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DeactivateTenantCommand(otherTenant))).IsSuccess.Should().BeTrue();
        }

        (await Count("SELECT count(*) FROM tenant.\"Tenants\" WHERE \"Id\" = @id AND NOT \"IsActive\"", ("id", otherTenant))).Should().Be(1);
    }

    [Fact]
    public async Task The_owner_link_table_enforces_row_level_security_for_the_application_role()
    {
        _ = await RegisterAsync(infrastructure.Api);

        await using var connection = new NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM tenant.\"TenantUsers\"", connection);

        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }
}
