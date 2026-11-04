using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Tenant.Application.Commands;

namespace RiskGuard.IntegrationTests.Tenant;

internal static class TenantTestHarness
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> TenantsByEmail = new(StringComparer.OrdinalIgnoreCase);

    public static string UniqueEmail() => $"lojista-{Guid.NewGuid():N}@example.com";

    /// <summary>Cadastra um lojista pelo caso de uso (como o comando de provisionamento): cria o lojista e o usuario dono no provedor (falso).</summary>
    public static async Task<(Guid TenantId, string Email)> RegisterAsync(
        ApiFactory factory,
        string? name = null,
        string? email = null,
        bool connectPlatforms = true)
    {
        email ??= UniqueEmail();
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new RegisterTenantCommand(name ?? "Loja Teste", email));

        result.IsSuccess.Should().BeTrue(because: result.IsSuccess ? string.Empty : result.Error.Message);
        var tenantId = result.Value.TenantId;
        TenantsByEmail[email] = tenantId;

        if (connectPlatforms)
        {
            await ConnectPlatformsAsync(factory, tenantId, email);
        }

        // Toda requisicao autenticada consulta (e portanto aquece) o snapshot do tenant no cache. Os testes ajustam o tenant direto
        // no banco logo apos o cadastro; devolvemos o cache ao estado "frio" para que essas alteracoes sejam vistas.
        await scope.ServiceProvider.GetRequiredService<RiskGuard.Core.Abstractions.ICacheService>().RemoveAsync($"tenant:{tenantId}:snapshot");

        return (tenantId, email);
    }

    /// <summary>Conecta Shopify e Nuvemshop pela API publica (como o lojista faria), com credenciais aleatorias por tenant.</summary>
    public static async Task ConnectPlatformsAsync(ApiFactory factory, Guid tenantId, string email)
    {
        using var client = await AuthenticatedClientAsync(factory, email);

        foreach (var platform in new[] { "shopify", "nuvemshop" })
        {
            var credentials = TestCredentialStore.NewCredentials(platform);
            var response = await client.PutAsJsonAsync(
                new Uri($"/api/v1/tenants/me/connections/{platform}", UriKind.Relative),
                new { accountId = credentials.AccountId, webhookSecret = credentials.WebhookSecret, accessToken = credentials.AccessToken });
            response.EnsureSuccessStatusCode();
            TestCredentialStore.Set(tenantId, platform, credentials);
        }
    }

    /// <summary>Token de acesso do dono do lojista, no formato do provedor (assinado pela chave de teste).</summary>
    public static string AccessTokenFor(string email) => TestIdentity.CreateToken(TenantsByEmail[email]);

    /// <summary>Cliente HTTP autenticado como o lojista.</summary>
    public static Task<HttpClient> AuthenticatedClientAsync(ApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessTokenFor(email));
        return Task.FromResult(client);
    }

    /// <summary>Altera o estado diretamente no banco com a conexao administrativa.</summary>
    public static async Task ExecuteAsAdminAsync(InfrastructureFixture infrastructure, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public sealed record TenantBody(Guid Id, string Name, string Email, SettingsBody Settings);

    public sealed record SettingsBody(int HighRiskThreshold, bool AutoBlockEnabled);
}
