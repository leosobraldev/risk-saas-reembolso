using System.Net.Mail;

namespace RiskGuard.Core.Domain;

/// <summary>Regra unica de normalizacao/validacao de e-mail compartilhada por todos os modulos.</summary>
public static class EmailRules
{
    public const int MaxLength = 254;

    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? email)
    {
        var normalized = Normalize(email);
        return normalized.Length is > 0 and <= MaxLength
            && MailAddress.TryCreate(normalized, out var address)
            && address.Address == normalized;
    }
}
