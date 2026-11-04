using Microsoft.Extensions.Options;
using RiskGuard.Api;
using RiskGuard.Api.Endpoints;
using RiskGuard.Api.Middleware;
using RiskGuard.Api.Provisioning;
using RiskGuard.Api.Security;
using RiskGuard.Infrastructure;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var httpSecurity = HttpSecurityOptions.From(builder.Configuration);
httpSecurity.Validate(); // fail-fast: proxy/CORS invalidos ou inseguros impedem a subida

builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

builder.Services.AddInfrastructure(builder.Configuration);

foreach (var module in ModuleCatalog.All)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

// CQRS + eventos de dominio: unica via de comunicacao entre modulos.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(ModuleCatalog.All.Select(m => m.Assembly).ToArray());
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ClientErrorExceptionHandler>();
builder.Services.AddHsts(hsts =>
{
    hsts.MaxAge = TimeSpan.FromDays(365);
    hsts.IncludeSubDomains = true;
});

if (httpSecurity.AllowedOrigins.Count > 0)
{
    builder.Services.AddCors(cors => cors.AddPolicy(CorsPolicies.Frontend, policy => policy
        .WithOrigins([.. httpSecurity.AllowedOrigins])
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .WithHeaders("Authorization", "Content-Type")
        .WithExposedHeaders("Retry-After")
        .SetPreflightMaxAge(TimeSpan.FromMinutes(10)))); // sem AllowCredentials: o token vai no header, nao em cookie
}

builder.Services.AddHealthChecks()
    .AddNpgSql(
        sp => sp.GetRequiredService<IOptions<PostgresOptions>>().Value.ConnectionString,
        name: "postgres",
        tags: ["ready"])
    .AddRedis(
        sp => sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString,
        name: "redis",
        tags: ["ready"]);

var app = builder.Build();

// Modo de deploy: aplica as migrations com a credencial administrativa e encerra. A API em producao roda SEM essa credencial.
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await app.Services.MigrateDatabasesAsync();
    return 0;
}

// Modo operacional: cadastra um lojista (e o usuario dono no provedor de identidade) e encerra.
if (args.Contains(ProvisionTenantCli.Flag, StringComparer.Ordinal))
{
    return await ProvisionTenantCli.RunAsync(app.Services, args);
}

if (app.Services.GetRequiredService<IOptions<PostgresOptions>>().Value.MigrateOnStartup)
{
    await app.Services.MigrateDatabasesAsync();
}

WarnIfAdminCredentialIsPresent(app);

// Primeiro de tudo: qualquer excecao vira ProblemDetails generico (sem stack, sem mensagem interna).
app.UseExceptionHandler();

// So com proxies configurados: o IP/esquema reais do cliente vem de X-Forwarded-*; do contrario esses headers sao ignorados.
if (httpSecurity.BuildForwardedHeadersOptions() is { } forwarded)
{
    app.UseForwardedHeaders(forwarded);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (httpSecurity.RedirectToHttps)
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();

if (httpSecurity.AllowedOrigins.Count > 0)
{
    app.UseCors(CorsPolicies.Frontend);
}

app.UseMiddleware<RateLimitingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<ActiveTenantMiddleware>();
app.UseAuthorization();
app.UseRiskGuardHangfireDashboard();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapTenantEndpoints();
app.MapWebhookEndpoints();
app.MapRiskEndpoints();
app.MapTenantManagementEndpoints();
app.MapActionEndpoints();
app.MapPrivacyEndpoints();

await app.RunAsync();
return 0;

static void WarnIfAdminCredentialIsPresent(WebApplication app)
{
    var postgres = app.Services.GetRequiredService<IOptions<PostgresOptions>>().Value;
    if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(postgres.JobsConnectionString))
    {
        app.Logger.LogWarning(
            "Postgres:JobsConnectionString nao esta configurada: o Hangfire usara outra credencial. Configure o papel dedicado (riskguard_jobs).");
    }

    if (!app.Environment.IsDevelopment() && !postgres.MigrateOnStartup && !string.IsNullOrWhiteSpace(postgres.AdminConnectionString))
    {
        app.Logger.LogWarning(
            "Postgres:AdminConnectionString esta configurada no processo da API. Em producao, execute as migrations em um passo separado (--migrate) e remova a credencial administrativa da API.");
    }
}

/// <summary>Marcador para o WebApplicationFactory dos testes de integracao.</summary>
public partial class Program;
