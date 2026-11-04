using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.Core.Contracts;
using RiskGuard.IntegrationTests.Infrastructure;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Platforms;

/// <summary>Credenciais por loja: o lojista conecta Shopify/Nuvemshop; segredos so entram (cifrados), nunca saem.</summary>
[Collection(InfrastructureCollection.Name)]
public class PlatformConnectionsTests(InfrastructureFixture infrastructure)
{
    private static readonly Uri List = new("/api/v1/tenants/me/connections", UriKind.Relative);

    private static Uri Platform(string platform) => new($"/api/v1/tenants/me/connections/{platform}", UriKind.Relative);

    private const string Secret = "whsec_plaintext_must_never_leak";
    private const string Token = "shpat_plaintext_must_never_leak";

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        using var anonymous = infrastructure.Api.CreateClient();

        (await anonymous.GetAsync(List)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync(Platform("shopify"), new { accountId = "a.myshopify.com", webhookSecret = "s" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync(Platform("shopify"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Connecting_returns_a_masked_view_and_no_response_ever_contains_the_secrets()
    {
        var (_, email) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(infrastructure.Api, email);

        var put = await client.PutAsJsonAsync(Platform("shopify"), new { accountId = "minha-loja.myshopify.com", webhookSecret = Secret, accessToken = Token });
        var putBody = await put.Content.ReadAsStringAsync();
        var listBody = await client.GetStringAsync(List);

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        putBody.Should().Contain("minha-loja.myshopify.com").And.Contain("\"hasAccessToken\":true");
        foreach (var body in new[] { putBody, listBody })
        {
            body.Should().NotContain(Secret).And.NotContain(Token).And.NotContainEquivalentOf("secret\":").And.NotContainEquivalentOf("\"accessToken");
        }

        (await client.GetFromJsonAsync<List<ConnectionBody>>(List))!.Should().ContainSingle().Which.Platform.Should().Be("shopify");
    }

    [Fact]
    public async Task Secrets_are_stored_encrypted_never_in_plaintext()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(infrastructure.Api, email);
        (await client.PutAsJsonAsync(Platform("shopify"), new { accountId = "x.myshopify.com", webhookSecret = Secret, accessToken = Token })).EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"WebhookSecretProtected\", \"AccessTokenProtected\" FROM tenant.\"PlatformConnections\" WHERE \"TenantId\" = @t", connection);
        command.Parameters.AddWithValue("t", tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        reader.GetString(0).Should().StartWith("v1:").And.NotContain(Secret);
        reader.GetString(1).Should().StartWith("v1:").And.NotContain(Token);
    }

    [Fact]
    public async Task Other_modules_read_the_decrypted_credentials_only_through_the_contract()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(infrastructure.Api, email);
        (await client.PutAsJsonAsync(Platform("shopify"), new { accountId = "x.myshopify.com", webhookSecret = Secret, accessToken = Token })).EnsureSuccessStatusCode();

        await using var scope = infrastructure.Api.Services.CreateAsyncScope();
        var credentials = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetPlatformCredentialsQuery(tenantId, "shopify"));

        credentials.Should().NotBeNull();
        credentials!.WebhookSecret.Should().Be(Secret);
        credentials.AccessToken.Should().Be(Token);
        credentials.AccountId.Should().Be("x.myshopify.com");
        credentials.ToString().Should().NotContain(Secret).And.NotContain(Token); // ToString nao vaza segredos em logs
    }

    [Fact]
    public async Task Reconnecting_replaces_the_single_row_and_can_drop_the_token()
    {
        var (tenantId, email) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(infrastructure.Api, email);
        await client.PutAsJsonAsync(Platform("nuvemshop"), new { accountId = "111", webhookSecret = "a", accessToken = "t" });

        var second = await client.PutAsJsonAsync(Platform("nuvemshop"), new { accountId = "222", webhookSecret = "b" });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await client.GetFromJsonAsync<List<ConnectionBody>>(List))!;
        list.Should().ContainSingle();
        list[0].AccountId.Should().Be("222");
        list[0].HasAccessToken.Should().BeFalse();

        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM tenant.\"PlatformConnections\" WHERE \"TenantId\" = @t", connection);
        count.Parameters.AddWithValue("t", tenantId);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(1);
    }

    [Theory]
    [InlineData("shopify", "evil.com", "s")]
    [InlineData("shopify", "loja.myshopify.com.evil.com", "s")]
    [InlineData("shopify", "https://loja.myshopify.com", "s")]
    [InlineData("shopify", "", "s")]
    [InlineData("shopify", null, "s")]
    [InlineData("nuvemshop", "abc", "s")]
    [InlineData("nuvemshop", "123/../x", "s")]
    [InlineData("shopify", "loja.myshopify.com", "")]
    [InlineData("shopify", "loja.myshopify.com", null)]
    public async Task Invalid_connections_are_rejected_with_400(string platform, string? accountId, string? webhookSecret)
    {
        var (_, email) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(infrastructure.Api, email);

        var response = await client.PutAsJsonAsync(Platform(platform), new { accountId, webhookSecret });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<List<ConnectionBody>>(List))!.Should().BeEmpty();
    }

    [Fact]
    public async Task Oversized_secrets_and_unsupported_platforms_are_rejected()
    {
        var (_, email) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(infrastructure.Api, email);

        (await client.PutAsJsonAsync(Platform("shopify"), new { accountId = "a.myshopify.com", webhookSecret = new string('x', 1001) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(Platform("shopify"), new { accountId = "a.myshopify.com", webhookSecret = "s", accessToken = new string('x', 1001) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(Platform("magento"), new { accountId = "1", webhookSecret = "s" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disconnecting_removes_the_connection()
    {
        var (_, email) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var client = await AuthenticatedClientAsync(infrastructure.Api, email);
        await client.PutAsJsonAsync(Platform("shopify"), new { accountId = "a.myshopify.com", webhookSecret = "s" });

        (await client.DeleteAsync(Platform("shopify"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<List<ConnectionBody>>(List))!.Should().BeEmpty();
        (await client.DeleteAsync(Platform("shopify"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_tenant_never_sees_or_touches_another_tenants_connections()
    {
        var (tenantA, emailA) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        var (tenantB, emailB) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var a = await AuthenticatedClientAsync(infrastructure.Api, emailA);
        using var b = await AuthenticatedClientAsync(infrastructure.Api, emailB);
        await a.PutAsJsonAsync(Platform("shopify"), new { accountId = "a.myshopify.com", webhookSecret = "segredo-de-A", accessToken = "token-de-A" });

        (await b.GetFromJsonAsync<List<ConnectionBody>>(List))!.Should().BeEmpty();
        (await b.DeleteAsync(Platform("shopify"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.GetFromJsonAsync<List<ConnectionBody>>(List))!.Should().ContainSingle();

        await using var scope = infrastructure.Api.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetPlatformCredentialsQuery(tenantB, "shopify"))).Should().BeNull();
        tenantA.Should().NotBe(tenantB);
    }

    [Fact]
    public async Task A_ciphertext_copied_to_another_tenants_row_cannot_be_decrypted()
    {
        var (tenantA, emailA) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        var (tenantB, emailB) = await RegisterAsync(infrastructure.Api, connectPlatforms: false);
        using var a = await AuthenticatedClientAsync(infrastructure.Api, emailA);
        using var b = await AuthenticatedClientAsync(infrastructure.Api, emailB);
        await a.PutAsJsonAsync(Platform("shopify"), new { accountId = "a.myshopify.com", webhookSecret = "segredo-de-A", accessToken = "token-de-A" });
        await b.PutAsJsonAsync(Platform("shopify"), new { accountId = "b.myshopify.com", webhookSecret = "segredo-de-B", accessToken = "token-de-B" });

        // atacante com acesso ao banco copia o segredo cifrado de A para a linha de B
        await ExecuteAsAdminAsync(
            infrastructure,
            """
            UPDATE tenant."PlatformConnections" b SET "WebhookSecretProtected" = a."WebhookSecretProtected"
            FROM tenant."PlatformConnections" a WHERE a."TenantId" = @a AND b."TenantId" = @b
            """,
            ("a", tenantA), ("b", tenantB));

        await using var scope = infrastructure.Api.Services.CreateAsyncScope();
        var act = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetPlatformCredentialsQuery(tenantB, "shopify"));

        await act.Should().ThrowAsync<CryptographicException>(); // o contexto (tenant) e autenticado junto: AEAD
    }

    [Fact]
    public async Task Connections_table_enforces_row_level_security_for_the_application_role()
    {
        var (_, email) = await RegisterAsync(infrastructure.Api);
        _ = email;

        await using var connection = new NpgsqlConnection(infrastructure.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM tenant.\"PlatformConnections\"", connection);

        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }

    private sealed record ConnectionBody(string Platform, string AccountId, bool HasAccessToken, DateTimeOffset UpdatedAt);
}
