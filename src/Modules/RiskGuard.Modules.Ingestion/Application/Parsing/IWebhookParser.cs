using RiskGuard.Core.Abstractions;

namespace RiskGuard.Modules.Ingestion.Application.Parsing;

/// <summary>Pedido ja normalizado, comum a todas as plataformas.</summary>
public sealed record NormalizedOrder(
    string Platform,
    string ExternalOrderId,
    string CustomerEmail,
    decimal TotalAmount,
    DateTimeOffset CreatedAt);

/// <summary>Strategy: uma implementacao por plataforma de e-commerce.</summary>
public interface IWebhookParser
{
    /// <summary>Identificador canonico (minusculo) usado na rota e persistido.</summary>
    string Platform { get; }

    Result<NormalizedOrder> Parse(string payload);

    /// <summary>
    /// Numero do pedido numa notificacao que traz so o id (para buscar o pedido na API da plataforma), ou <c>null</c> se o payload
    /// nao for legivel. Por padrao, o campo <c>id</c> da raiz.
    /// </summary>
    string? ReadNotificationOrderId(string payload);
}

/// <summary>Seleciona a Strategy pela plataforma informada na rota.</summary>
public interface IWebhookParserResolver
{
    IWebhookParser? Resolve(string? platform);
}
