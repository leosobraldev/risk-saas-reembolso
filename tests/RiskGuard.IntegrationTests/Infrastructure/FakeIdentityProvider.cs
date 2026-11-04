using System.Collections.Concurrent;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.IntegrationTests.Infrastructure;

public sealed record FakeIdentityUser(string ExternalId, string Email, Guid TenantId)
{
    public bool Blocked { get; set; }
}

/// <summary>
/// Provedor de identidade em memoria no lugar do Auth0: guarda os usuarios provisionados (com o tenant em metadata) e permite
/// programar falhas para exercitar a compensacao do cadastro.
/// </summary>
public sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly ConcurrentDictionary<string, FakeIdentityUser> _users = new();
    private readonly ConcurrentQueue<string> _deleted = new();
    private Error? _failNextCreate;
    private Error? _failNextBlock;
    private Error? _failNextDelete;

    /// <summary>Forca o id do proximo usuario criado (para provocar conflitos no banco e exercitar a compensacao).</summary>
    public string? NextUserId { get; set; }

    public string Name => "Fake";

    public IReadOnlyCollection<FakeIdentityUser> Users => _users.Values.ToList();

    public IReadOnlyCollection<string> DeletedUserIds => _deleted.ToList();

    public FakeIdentityUser? FindByEmail(string email) =>
        _users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));

    public void FailNextCreate(Error error) => _failNextCreate = error;

    public void FailNextBlock(Error error) => _failNextBlock = error;

    public void FailNextDelete(Error error) => _failNextDelete = error;

    public Task<Result<CreatedIdentityUser>> CreateUserAsync(CreateIdentityUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Interlocked.Exchange(ref _failNextCreate, null) is { } failure)
        {
            return Task.FromResult<Result<CreatedIdentityUser>>(failure);
        }

        if (FindByEmail(request.Email) is not null)
        {
            return Task.FromResult<Result<CreatedIdentityUser>>(IdentityErrors.UserAlreadyExists);
        }

        var user = new FakeIdentityUser(NextUserId ?? $"auth0|{Guid.NewGuid():N}", request.Email, request.TenantId);
        NextUserId = null;
        _users[user.ExternalId] = user;
        return Task.FromResult<Result<CreatedIdentityUser>>(
            new CreatedIdentityUser(user.ExternalId, user.Email, new Uri($"https://fake-idp.test/ticket/{Guid.NewGuid():N}")));
    }

    public Task<Result> SetBlockedAsync(string externalUserId, bool blocked, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _failNextBlock, null) is { } failure)
        {
            return Task.FromResult(Result.Failure(failure));
        }

        if (_users.TryGetValue(externalUserId, out var user))
        {
            user.Blocked = blocked;
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeleteUserAsync(string externalUserId, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _failNextDelete, null) is { } failure)
        {
            return Task.FromResult(Result.Failure(failure));
        }

        if (_users.TryRemove(externalUserId, out _))
        {
            _deleted.Enqueue(externalUserId);
        }

        return Task.FromResult(Result.Success());
    }
}
