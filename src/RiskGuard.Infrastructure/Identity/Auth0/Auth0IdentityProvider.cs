using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Infrastructure.Identity.Auth0;

public static class Auth0Registration
{
    internal static IServiceCollection AddAuth0(this IServiceCollection services)
    {
        services.AddHttpClient(Auth0ManagementClient.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<Auth0ManagementClient>();
        services.AddSingleton<IIdentityProvider, Auth0IdentityProvider>();
        return services;
    }
}

/// <summary>
/// Adaptador do Auth0 (Management API v2). O usuario e criado SEM senha conhecida (uma aleatoria que ninguem ve) e recebe o
/// <c>tenant_id</c> em <c>app_metadata</c>; uma Action de post-login (ver docs/auth0-setup.md) o copia para o token de acesso
/// no claim namespaced. O usuario define a propria senha por um ticket de uso unico.
/// </summary>
internal sealed partial class Auth0IdentityProvider(Auth0ManagementClient client, IOptions<IdentityOptions> options) : IIdentityProvider
{
    public string Name => IdentityOptions.Auth0Provider;

    public async Task<Result<CreatedIdentityUser>> CreateUserAsync(CreateIdentityUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.Value.Auth0;

        try
        {
            var created = await client.SendAsync(
                HttpMethod.Post,
                "users",
                new Dictionary<string, object?>
                {
                    ["connection"] = settings.Connection,
                    ["email"] = request.Email,
                    ["name"] = request.Name,
                    ["password"] = GenerateUnknownPassword(),
                    ["email_verified"] = false,
                    ["verify_email"] = false,
                    ["app_metadata"] = new Dictionary<string, string> { ["tenant_id"] = request.TenantId.ToString() },
                },
                cancellationToken).ConfigureAwait(false);

            switch (created.Status)
            {
                case HttpStatusCode.Created:
                    break;
                case HttpStatusCode.Conflict:
                    return IdentityErrors.UserAlreadyExists;
                case HttpStatusCode.BadRequest:
                    return IdentityErrors.Rejected("dados do usuario invalidos (verifique a conexao configurada).");
                default:
                    return IdentityErrors.Unavailable($"HTTP {(int)created.Status} ao criar o usuario.");
            }

            using var document = JsonDocument.Parse(created.Body);
            var userId = document.RootElement.GetProperty("user_id").GetString();
            if (!IsSafeUserId(userId))
            {
                return IdentityErrors.Unavailable("resposta do Auth0 sem user_id valido.");
            }

            return new CreatedIdentityUser(userId!, request.Email, await TryCreatePasswordTicketAsync(userId!, settings, cancellationToken).ConfigureAwait(false));
        }
        catch (Auth0UnavailableException ex)
        {
            return IdentityErrors.Unavailable(ex.Message);
        }
        catch (JsonException)
        {
            return IdentityErrors.Unavailable("resposta invalida do Auth0.");
        }
    }

    public Task<Result> SetBlockedAsync(string externalUserId, bool blocked, CancellationToken cancellationToken = default) =>
        ModifyUserAsync(HttpMethod.Patch, externalUserId, new Dictionary<string, object> { ["blocked"] = blocked }, cancellationToken);

    public Task<Result> DeleteUserAsync(string externalUserId, CancellationToken cancellationToken = default) =>
        ModifyUserAsync(HttpMethod.Delete, externalUserId, body: null, cancellationToken);

    private async Task<Result> ModifyUserAsync(HttpMethod method, string externalUserId, object? body, CancellationToken cancellationToken)
    {
        if (!IsSafeUserId(externalUserId))
        {
            return Result.Failure(IdentityErrors.Rejected("identificador de usuario invalido."));
        }

        try
        {
            var response = await client.SendAsync(method, $"users/{Uri.EscapeDataString(externalUserId)}", body, cancellationToken).ConfigureAwait(false);

            // 404: o usuario ja nao existe -> o estado desejado (bloqueado/removido) ja vale; a operacao e idempotente.
            return response.Status is HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.NotFound
                ? Result.Success()
                : Result.Failure(IdentityErrors.Unavailable($"HTTP {(int)response.Status} ao alterar o usuario."));
        }
        catch (Auth0UnavailableException ex)
        {
            return Result.Failure(IdentityErrors.Unavailable(ex.Message));
        }
    }

    /// <summary>O link e o unico caminho para o usuario definir senha; se nao sair, o usuario existe e o operador reenvia pelo painel do Auth0.</summary>
    private async Task<Uri?> TryCreatePasswordTicketAsync(string userId, Auth0Options settings, CancellationToken cancellationToken)
    {
        try
        {
            var ticket = await client.SendAsync(
                HttpMethod.Post,
                "tickets/password-change",
                new Dictionary<string, object> { ["user_id"] = userId, ["ttl_sec"] = settings.PasswordTicketTtlSeconds },
                cancellationToken).ConfigureAwait(false);

            if (ticket.Status != HttpStatusCode.Created)
            {
                return null;
            }

            using var document = JsonDocument.Parse(ticket.Body);
            return document.RootElement.TryGetProperty("ticket", out var url)
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var ticketUrl)
                && ticketUrl.Scheme == Uri.UriSchemeHttps
                ? ticketUrl
                : null;
        }
        catch (Exception ex) when (ex is Auth0UnavailableException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Senha aleatoria que ninguem conhece, com as classes de caracteres que a politica padrao do Auth0 exige.</summary>
    private static string GenerateUnknownPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=') + "aA1!";

    private static bool IsSafeUserId(string? userId) => userId is { Length: > 0 and <= 128 } && SafeUserId().IsMatch(userId);

    [GeneratedRegex(@"^[A-Za-z0-9_\-|.]+$")]
    private static partial Regex SafeUserId();
}
