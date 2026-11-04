using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Domain;

namespace RiskGuard.Modules.Tenant.Domain;

/// <summary>Lojista (assinante do SaaS). Raiz do isolamento multi-tenant. Credenciais de login vivem no provedor de identidade, nao aqui.</summary>
public sealed class Tenant : Entity
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = EmailRules.MaxLength;

    private Tenant()
    {
        // EF Core
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>E-mail de contato do dono (normalizado). Identifica o cadastro; o login em si e do provedor.</summary>
    public string Email { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Tenant> Create(string? name, string? email, DateTimeOffset now)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            return new Error("tenant.invalid_name", $"O nome deve ter entre 1 e {NameMaxLength} caracteres.", ErrorType.Validation);
        }

        if (!EmailRules.IsValid(email))
        {
            return new Error("tenant.invalid_email", "E-mail invalido.", ErrorType.Validation);
        }

        return Result<Tenant>.Success(new Tenant
        {
            Name = trimmedName,
            Email = EmailRules.Normalize(email),
            IsActive = true,
            CreatedAt = now,
        });
    }

    public void Deactivate() => IsActive = false;
}
