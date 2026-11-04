using System.Globalization;
using Hangfire.Logging;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using RiskGuard.Core.Abstractions;
using RiskGuard.Core.Contracts;

namespace RiskGuard.IntegrationTests.Infrastructure;

public sealed class ApiFactory(
    InfrastructureFixture infrastructure,
    string environment = "Development",
    int permitLimit = 1000,
    int webhookTenantPermitLimit = 1000,
    bool hangfireServerEnabled = true,
    int hangfireMaxRetries = 5,
    IReadOnlyDictionary<string, string>? extraSettings = null,
    string? database = null,
    bool leastPrivilegeTopology = false) : WebApplicationFactory<Program>
{
    // O Hangfire guarda o LoggerFactory do ULTIMO host criado em um campo estatico. Ao descartar esse host, os workers
    // de todos os outros hosts do processo falham com ObjectDisposedException('LoggerFactory') e param de buscar jobs.
    // Em producao ha um unico host; nos testes fixamos um provider que nao depende de nenhum host.
    /// <summary>Schema do Hangfire deste host (unico por host de teste).</summary>
    public string HangfireSchema { get; } = $"hangfire_{Guid.NewGuid():N}";

    private string Connection(string connectionString) =>
        database is null ? connectionString : new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        LogProvider.SetCurrentLogProvider(StableHangfireLogProvider.Instance);
        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);

        // A API roda com o papel restrito (sujeito a RLS); migrations e Hangfire usam a conexao do dono.
        builder.UseSetting("Postgres:ConnectionString", Connection(infrastructure.AppConnectionString));
        builder.UseSetting("Postgres:JobsConnectionString", Connection(infrastructure.JobsConnectionString));

        // Topologia de producao: a API NAO recebe a credencial de dono (as migrations rodaram antes, em --migrate).
        // (valores vazios sobrescrevem os de appsettings.Development.json)
        builder.UseSetting("Postgres:AdminConnectionString", leastPrivilegeTopology ? string.Empty : Connection(infrastructure.PostgresConnectionString));
        builder.UseSetting("Postgres:MigrateOnStartup", leastPrivilegeTopology ? "false" : "true");
        builder.UseSetting("Redis:ConnectionString", infrastructure.RedisConnectionString);
        builder.UseSetting("Redis:InstanceName", $"it-{Guid.NewGuid():N}:"); // contadores/cache isolados por host de teste
        builder.UseSetting("RateLimiting:PermitLimit", permitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:WebhookTenantPermitLimit", webhookTenantPermitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Hangfire:WorkerCount", "2");
        builder.UseSetting("Hangfire:SchemaName", HangfireSchema); // filas isoladas por host de teste
        builder.UseSetting("Hangfire:ServerEnabled", hangfireServerEnabled ? "true" : "false");
        builder.UseSetting("Hangfire:MaxRetryAttempts", hangfireMaxRetries.ToString(CultureInfo.InvariantCulture));

        // Segredos de terceiros cifrados com uma chave de teste (32 bytes em base64).
        builder.UseSetting("Security:EncryptionKey", "dGVzdC1vbmx5LWFlcy1rZXktMzItYnl0ZXMtISEhISE=");

        // APIs das plataformas FALSAS: o handler de teste substitui a rede (HttpClientFactory real, sem trafego externo).
        builder.UseSetting("PlatformApis:Platforms:shopify:BaseUrl", "https://shopify.sim.test/");
        builder.UseSetting("PlatformApis:Platforms:nuvemshop:BaseUrl", "https://nuvemshop.sim.test/");

        // Captura os eventos publicados via MediatR (prova a comunicacao entre modulos sem depender do RiskModule).
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<OrderIngestedCapture>();
            services.AddSingleton<INotificationHandler<OrderIngestedEvent>>(sp => sp.GetRequiredService<OrderIngestedCapture>());
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();

            // Provedor de identidade falso (nunca chama o Auth0) e autoridade de tokens de teste (sem descoberta OIDC pela rede).
            services.RemoveAll<IIdentityProvider>();
            services.AddSingleton<FakeIdentityProvider>();
            services.AddSingleton<IIdentityProvider>(sp => sp.GetRequiredService<FakeIdentityProvider>());
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, bearer =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = TestIdentity.Issuer };
                configuration.SigningKeys.Add(TestIdentity.SigningKey);
                // O handler le as chaves do ConfigurationManager: aqui estatico (sem descoberta OIDC pela rede).
                bearer.Configuration = configuration;
                bearer.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            services.AddSingleton<FakePlatformsHandler>();
            foreach (var platform in new[] { "shopify", "nuvemshop" })
            {
                services.AddHttpClient(platform).ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<FakePlatformsHandler>());
            }

            services.AddSingleton<RiskCalculatedCapture>();
            services.AddSingleton<INotificationHandler<RiskCalculatedEvent>>(sp => sp.GetRequiredService<RiskCalculatedCapture>());
        });
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        // Identidade: o provedor (Auth0) e substituido por um emissor RSA de teste e por um provedor falso (ver TestIdentity).
        builder.UseSetting("Identity:Audience", TestIdentity.Audience);
        builder.UseSetting("Identity:TenantClaim", TestIdentity.TenantClaim);
        builder.UseSetting("Identity:Auth0:Domain", "riskguard-tests.us.auth0.com");
        builder.UseSetting("Identity:Auth0:ClientId", "test-client-id");
        builder.UseSetting("Identity:Auth0:ClientSecret", "test-client-secret");
    }
}

public sealed class OrderIngestedCapture : INotificationHandler<OrderIngestedEvent>
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<OrderIngestedEvent> _events = new();

    public IReadOnlyCollection<OrderIngestedEvent> Events => _events;

    public Task Handle(OrderIngestedEvent notification, CancellationToken cancellationToken)
    {
        _events.Enqueue(notification);
        return Task.CompletedTask;
    }
}

/// <summary>Faz o papel do futuro ActionModule: observa os RiskCalculatedEvent publicados pelo RiskModule.</summary>
public sealed class RiskCalculatedCapture : INotificationHandler<RiskCalculatedEvent>
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<RiskCalculatedEvent> _events = new();

    public IReadOnlyCollection<RiskCalculatedEvent> Events => _events;

    public Task Handle(RiskCalculatedEvent notification, CancellationToken cancellationToken)
    {
        _events.Enqueue(notification);
        return Task.CompletedTask;
    }
}


/// <summary>Provider de log do Hangfire que nao pertence a nenhum host (ver <see cref="ApiFactory"/>).</summary>
internal sealed class StableHangfireLogProvider : ILogProvider
{
    public static StableHangfireLogProvider Instance { get; } = new();

    public ILog GetLogger(string name) => SilentLog.Instance;

    private sealed class SilentLog : ILog
    {
        public static SilentLog Instance { get; } = new();

        public bool Log(Hangfire.Logging.LogLevel logLevel, Func<string>? messageFunc, Exception? exception = null) => true;
    }
}

/// <summary>
/// O TestServer nao tem IP de origem. Este filtro (so de teste) o define pelo header <c>X-Test-Remote-Ip</c>, ANTES de qualquer
/// middleware da aplicacao, para exercitar o tratamento de proxies e de X-Forwarded-*.
/// </summary>
internal sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var ip) && System.Net.IPAddress.TryParse(ip.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware();
            });

            next(app);
        };
}
