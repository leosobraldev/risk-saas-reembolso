using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Identity.Auth0;

namespace RiskGuard.Infrastructure.Identity;

public static class IdentityAuthentication
{
    public const string TenantPolicy = "Tenant";

    /// <summary>Registra a validacao dos tokens do provedor e o adaptador de provisionamento escolhido.</summary>
    public static IServiceCollection AddIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(TimeProvider.System);

        services.AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdentityOptions>, IdentityOptionsValidator>());

        // Para suportar outro fornecedor: um novo adaptador de IIdentityProvider e um case aqui.
        var provider = configuration.GetValue<string>($"{IdentityOptions.SectionName}:Provider") ?? IdentityOptions.Auth0Provider;
        switch (provider)
        {
            case IdentityOptions.Auth0Provider:
                services.AddAuth0();
                break;
            default:
                throw new InvalidOperationException($"Identity:Provider '{provider}' nao e suportado.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<IdentityOptions>>(ConfigureJwtBearer);

        services.AddAuthorizationBuilder()
            .AddPolicy(TenantPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(RiskGuardClaims.TenantId));

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions bearer, IOptions<IdentityOptions> identityOptions)
    {
        var identity = identityOptions.Value;

        // Descoberta OIDC: emissor e chaves (JWKS) vem do provedor e sao renovados automaticamente (rotacao de chaves sem deploy).
        bearer.Authority = identity.EffectiveAuthority;
        bearer.Audience = identity.Audience;
        bearer.RequireHttpsMetadata = true;
        bearer.MapInboundClaims = false; // mantem "sub" e os demais claims como o provedor os emitiu
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = identity.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], // tokens de usuario do Auth0 sao RS256: nunca "none" nem HS*
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
        };
        bearer.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                MapTenantClaim(context.Principal, identity.TenantClaim);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// Converte o claim do provedor (namespaced) no claim interno <c>tenant_id</c>, unico que o resto do sistema le. Antes, REMOVE
    /// qualquer <c>tenant_id</c> que ja venha no token: um provedor/uma regra mal configurada nao pode permitir que o proprio
    /// token declare o tenant por um nome que o RiskGuard confia. Sem claim valido, o usuario fica sem tenant (a policy nega).
    /// </summary>
    internal static void MapTenantClaim(ClaimsPrincipal? principal, string providerClaimType)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        foreach (var spoofed in identity.FindAll(RiskGuardClaims.TenantId).ToList())
        {
            identity.RemoveClaim(spoofed);
        }

        var values = identity.FindAll(providerClaimType).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        if (values.Count == 1 && Guid.TryParse(values[0], out var tenantId) && tenantId != Guid.Empty)
        {
            identity.AddClaim(new Claim(RiskGuardClaims.TenantId, tenantId.ToString()));
        }
    }
}
