namespace RiskGuard.Modules.Ingestion.Application.Parsing;

public sealed class WebhookParserResolver : IWebhookParserResolver
{
    private readonly Dictionary<string, IWebhookParser> _parsers = new(StringComparer.OrdinalIgnoreCase);

    public WebhookParserResolver(IEnumerable<IWebhookParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);

        foreach (var parser in parsers)
        {
            if (!_parsers.TryAdd(parser.Platform, parser))
            {
                throw new InvalidOperationException($"Plataforma '{parser.Platform}' registrada mais de uma vez.");
            }
        }
    }

    public IWebhookParser? Resolve(string? platform) =>
        platform is not null && _parsers.TryGetValue(platform.Trim(), out var parser) ? parser : null;
}
