using Microsoft.Extensions.Options;

namespace RiskGuard.Infrastructure.Identity;

/// <summary>Configuracao de identidade. Tudo que e especifico de um fornecedor fica em subsecoes proprias (hoje, <see cref="Auth0"/>).</summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    public const string Auth0Provider = "Auth0";

    /// <summary>Qual adaptador de <c>IIdentityProvider</c> usar.</summary>
    public string Provider { get; init; } = Auth0Provider;

    /// <summary>
    /// Emissor dos tokens (URL com barra final). Vazio: derivado do provedor (Auth0: <c>https://{Domain}/</c>). As chaves de
    /// assinatura vem do JWKS descoberto nesta URL e giram sozinhas: o RiskGuard nunca guarda chave de assinatura.
    /// </summary>
    public string? Authority { get; init; }

    /// <summary>Identificador da API no provedor (claim <c>aud</c>). Tokens emitidos para outra audiencia sao recusados.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Nome do claim que o provedor coloca no token com o id do lojista. No Auth0 precisa ser um namespace do tipo URL
    /// (claims privados sem namespace sao descartados). O RiskGuard o converte para o claim interno <c>tenant_id</c>.
    /// </summary>
    public string TenantClaim { get; init; } = "https://riskguard.app/tenant_id";

    public Auth0Options Auth0 { get; init; } = new();

    public string EffectiveAuthority =>
        !string.IsNullOrWhiteSpace(Authority) ? Authority : $"https://{Auth0.Domain}/";
}

public sealed class Auth0Options
{
    /// <summary>Dominio do tenant Auth0, sem esquema (ex.: <c>minha-empresa.us.auth0.com</c>).</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>Aplicacao Machine-to-Machine autorizada na Management API (escopos: create:users, update:users, delete:users, create:user_tickets).</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>SEGREDO. Nunca versionar: user-secrets em desenvolvimento, gerenciador de segredos em producao.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Conexao de banco de dados do Auth0 onde os usuarios sao criados.</summary>
    public string Connection { get; init; } = "Username-Password-Authentication";

    /// <summary>Validade do link para o usuario definir a senha (padrao do Auth0: 5 dias).</summary>
    public int PasswordTicketTtlSeconds { get; init; } = 432_000;
}

/// <summary>Falha na subida (fail-fast) se a identidade estiver ausente ou insegura.</summary>
internal sealed class IdentityOptionsValidator : IValidateOptions<IdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (!string.Equals(options.Provider, IdentityOptions.Auth0Provider, StringComparison.Ordinal))
        {
            errors.Add($"Identity:Provider '{options.Provider}' nao e suportado (suportados: {IdentityOptions.Auth0Provider}).");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            errors.Add("Identity:Audience e obrigatorio (identificador da API no provedor).");
        }

        if (string.IsNullOrWhiteSpace(options.TenantClaim))
        {
            errors.Add("Identity:TenantClaim e obrigatorio.");
        }

        if (options.Auth0.Domain.Contains("://", StringComparison.Ordinal) || options.Auth0.Domain.Contains('/', StringComparison.Ordinal))
        {
            errors.Add("Identity:Auth0:Domain deve ser apenas o dominio, sem esquema nem caminho.");
        }

        if (options.Provider == IdentityOptions.Auth0Provider)
        {
            Require(errors, options.Auth0.Domain, "Identity:Auth0:Domain");
            Require(errors, options.Auth0.ClientId, "Identity:Auth0:ClientId");
            Require(errors, options.Auth0.ClientSecret, "Identity:Auth0:ClientSecret");
            Require(errors, options.Auth0.Connection, "Identity:Auth0:Connection");
        }

        if (options.Auth0.PasswordTicketTtlSeconds is < 60 or > 2_592_000)
        {
            errors.Add("Identity:Auth0:PasswordTicketTtlSeconds deve estar entre 60 e 2592000.");
        }

        // Os tokens e o JWKS so podem ser buscados/aceitos por HTTPS (evita man-in-the-middle no JWKS).
        if (string.IsNullOrWhiteSpace(options.Authority) && string.IsNullOrWhiteSpace(options.Auth0.Domain))
        {
            errors.Add("Informe Identity:Authority ou Identity:Auth0:Domain.");
        }
        else if (!Uri.TryCreate(options.EffectiveAuthority, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Identity:Authority deve ser uma URL HTTPS valida.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Require(List<string> errors, string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} e obrigatorio.");
        }
    }
}
