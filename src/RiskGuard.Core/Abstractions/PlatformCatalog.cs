namespace RiskGuard.Core.Abstractions;

/// <summary>
/// Como o painel e a API descrevem uma plataforma de e-commerce. Fonte unica das diferencas entre plataformas que a interface
/// precisa conhecer: o painel le isto em <c>GET /api/v1/platforms</c>, entao suportar uma plataforma nova nao exige mexer nele.
/// </summary>
/// <param name="Id">Identificador canonico (minusculo), usado na rota do webhook e persistido.</param>
/// <param name="Name">Nome para exibir.</param>
/// <param name="AccountIdLabel">Rotulo do identificador da loja na plataforma (ex.: "Dominio da loja").</param>
/// <param name="AccountIdHint">Exemplo do identificador.</param>
/// <param name="RequiresAccessToken">
/// O webhook nao traz os dados do pedido (so o id), entao sem o token de acesso o pedido nao pode ser buscado: a conexao exige o token.
/// </param>
/// <param name="SupportsActions">A plataforma permite ao RiskGuard reter, sinalizar e liberar pedidos. Falso: so avalia e recomenda.</param>
/// <param name="WebhookSecretHelp">Como a plataforma usa o segredo do webhook (quando nao e o padrao de assinar o corpo).</param>
public sealed record PlatformInfo(
    string Id,
    string Name,
    string AccountIdLabel,
    string AccountIdHint,
    bool RequiresAccessToken,
    bool SupportsActions,
    string? WebhookSecretHelp = null);

public static class PlatformCatalog
{
    public static IReadOnlyList<PlatformInfo> All { get; } =
    [
        new(PlatformIds.Shopify, "Shopify", "Domínio da loja", "minha-loja.myshopify.com", RequiresAccessToken: false, SupportsActions: true),
        new(PlatformIds.Nuvemshop, "Nuvemshop", "Id da loja", "Número da loja na Nuvemshop", RequiresAccessToken: false, SupportsActions: true),
        new(
            PlatformIds.Wake,
            "Wake",
            "Nome da loja",
            "Nome da loja na Wake (campo \"loja\" do webhook)",
            RequiresAccessToken: true,
            SupportsActions: false,
            WebhookSecretHelp: "A Wake não assina o corpo do webhook. Ao inscrever o webhook, cadastre o cabeçalho X-RiskGuard-Token com este mesmo valor."),
    ];

    public static PlatformInfo? Find(string? platform) =>
        platform is null ? null : All.FirstOrDefault(p => p.Id.Equals(platform.Trim(), StringComparison.OrdinalIgnoreCase));
}
