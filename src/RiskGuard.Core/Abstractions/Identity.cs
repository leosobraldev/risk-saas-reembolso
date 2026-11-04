namespace RiskGuard.Core.Abstractions;

/// <param name="TenantId">Gravado no provedor (metadata do usuario) e devolvido como claim no token: e como o provedor diz "este usuario pertence a este lojista".</param>
public sealed record CreateIdentityUserRequest(string Email, string? Name, Guid TenantId);

/// <param name="ExternalId">Identificador do usuario NO provedor (claim <c>sub</c> dos tokens).</param>
/// <param name="PasswordSetupUrl">Link de uso unico para o usuario definir a propria senha; e uma credencial, entao so deve ser entregue ao dono e nunca logado. Nulo se o provedor nao conseguiu gerar.</param>
public sealed record CreatedIdentityUser(string ExternalId, string Email, Uri? PasswordSetupUrl);

/// <summary>
/// Porta do provedor de identidade (Auth0 hoje). O RiskGuard NAO guarda senhas, nao emite tokens e nao implementa MFA, refresh,
/// recuperacao de senha nem deteccao de forca bruta: isso e do provedor. Aqui ficam so as operacoes que o RiskGuard precisa pedir
/// a ele (provisionar e bloquear usuarios). Trocar de fornecedor = novo adaptador + uma linha de registro.
/// </summary>
public interface IIdentityProvider
{
    string Name { get; }

    /// <summary>Cria o usuario dono de um lojista, sem senha conhecida (ele define a propria pelo link devolvido).</summary>
    Task<Result<CreatedIdentityUser>> CreateUserAsync(CreateIdentityUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Bloqueia (ou desbloqueia) o login do usuario no provedor. Idempotente.</summary>
    Task<Result> SetBlockedAsync(string externalUserId, bool blocked, CancellationToken cancellationToken = default);

    /// <summary>Remove o usuario do provedor (usado para compensar um provisionamento que falhou no meio). Idempotente.</summary>
    Task<Result> DeleteUserAsync(string externalUserId, CancellationToken cancellationToken = default);
}

public static class IdentityErrors
{
    public static readonly Error UserAlreadyExists =
        new("identity.user_already_exists", "Ja existe um usuario com este e-mail no provedor de identidade.", ErrorType.Conflict);

    public static Error Unavailable(string detail) =>
        new("identity.unavailable", $"Provedor de identidade indisponivel ou mal configurado: {detail}", ErrorType.Unavailable);

    public static Error Rejected(string detail) =>
        new("identity.rejected", $"O provedor de identidade recusou a operacao: {detail}", ErrorType.Validation);
}
