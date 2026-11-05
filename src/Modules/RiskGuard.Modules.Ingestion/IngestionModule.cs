using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Persistence;
using RiskGuard.Modules.Ingestion.Application;
using RiskGuard.Modules.Ingestion.Application.Parsing;
using RiskGuard.Modules.Ingestion.Application.Retention;
using RiskGuard.Modules.Ingestion.Application.Signatures;
using RiskGuard.Modules.Ingestion.Jobs;
using RiskGuard.Modules.Ingestion.Persistence;

namespace RiskGuard.Modules.Ingestion;

/// <summary>Recepcao de webhooks, normalizacao de pedidos (Template Method + Strategy). Schema PostgreSQL: <c>ingestion</c>.</summary>
public sealed class IngestionModule : IModule
{
    public string Name => "Ingestion";

    public Assembly Assembly => typeof(IngestionModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<IngestionDbContext>(IngestionDbContext.SchemaName);

        // Strategy por plataforma: suportar uma nova plataforma = adicionar um parser aqui.
        services.AddSingleton<IWebhookParser, ShopifyWebhookParser>();
        services.AddSingleton<IWebhookParser, NuvemshopWebhookParser>();
        services.AddSingleton<IWebhookParser, WakeWebhookParser>();
        services.AddSingleton<IWebhookParserResolver, WebhookParserResolver>();

        // Strategy de autenticidade (HMAC) por plataforma e busca de pedidos quando o webhook traz so o id.
        services.AddSingleton<IWebhookSignatureVerifier, ShopifySignatureVerifier>();
        services.AddSingleton<IWebhookSignatureVerifier, NuvemshopSignatureVerifier>();
        services.AddSingleton<IWebhookSignatureVerifier, WakeTokenVerifier>();
        services.AddSingleton<IWebhookSignatureVerifierResolver, WebhookSignatureVerifierResolver>();
        services.AddSingleton<IPlatformOrderFetcher, NuvemshopOrderFetcher>();
        services.AddSingleton<IPlatformOrderFetcher, WakeOrderFetcher>();

        services.AddScoped<ProcessWebhookJob>();

        // Retencao do payload bruto (dados pessoais): limpeza recorrente.
        services.AddScoped<PurgeExpiredRawPayloadsJob>();
        services.AddHostedService<RetentionJobRegistrar>();
    }
}
