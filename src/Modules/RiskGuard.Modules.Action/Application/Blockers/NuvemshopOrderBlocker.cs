using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.Infrastructure.Options;
using RiskGuard.Modules.Action.Domain;

namespace RiskGuard.Modules.Action.Application.Blockers;

/// <summary>
/// Nuvemshop NAO tem hold de pedido (a API so conhece open/closed/cancelled), e cancelar seria destrutivo (cancela e pode
/// estornar). Por isso a acao e <see cref="OrderActionType.Flag"/>: acrescenta uma nota interna <c>[RiskGuard]</c> ao pedido
/// para o lojista revisar antes de enviar, preservando a nota que ja existir. Idempotente: nao duplica a marca.
/// API: <c>https://api.nuvemshop.com.br/v1/{store_id}</c>, header <c>Authentication: bearer</c> e User-Agent obrigatorio.
/// </summary>
public sealed partial class NuvemshopOrderBlocker(IHttpClientFactory httpClientFactory, IOptions<PlatformApisOptions> options) : IOrderBlocker
{
    public const string Marker = "[RiskGuard]";

    private static readonly Uri DefaultBase = new("https://api.nuvemshop.com.br/");

    public string Platform => PlatformIds.Nuvemshop;

    public OrderActionType ActionType => OrderActionType.Flag;

    public async Task<ExternalApiResult> BlockAsync(BlockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await ReadOrderAsync(request, cancellationToken).ConfigureAwait(false);
        if (current.Failure is not null)
        {
            return current.Failure;
        }

        if (current.Status is "closed" or "cancelled")
        {
            return ExternalApiResult.Permanent($"Pedido ja esta '{current.Status}' na Nuvemshop; nada a sinalizar.");
        }

        if (current.Note.Contains(Marker, StringComparison.Ordinal))
        {
            return ExternalApiResult.Success(); // ja sinalizado
        }

        var line = $"{Marker} Risco alto (score {request.Score}): revise o pedido antes de enviar.";
        var note = string.IsNullOrWhiteSpace(current.Note) ? line : $"{current.Note.TrimEnd()}\n{line}";
        return await WriteNoteAsync(request, note, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalApiResult> ReleaseAsync(BlockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await ReadOrderAsync(request, cancellationToken).ConfigureAwait(false);
        if (current.Failure is not null)
        {
            return current.Failure;
        }

        if (!current.Note.Contains(Marker, StringComparison.Ordinal))
        {
            return ExternalApiResult.Success(); // nada a remover
        }

        var kept = current.Note
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith(Marker, StringComparison.Ordinal));
        return await WriteNoteAsync(request, string.Join('\n', kept).Trim(), cancellationToken).ConfigureAwait(false);
    }

    // ---- infraestrutura HTTP ----

    private sealed record OrderState(string Status, string Note, ExternalApiResult? Failure);

    private bool TryValidate(BlockRequest request, out string? error)
    {
        if (string.IsNullOrWhiteSpace(request.Credentials.AccessToken))
        {
            error = "Lojista sem token de acesso da Nuvemshop: conecte a loja para habilitar a sinalizacao.";
            return false;
        }

        if (!PlatformIds.IsValidAccountId(Platform, request.Credentials.AccountId) || !OrderIdPattern().IsMatch(request.ExternalOrderId))
        {
            error = "Identificador de loja ou de pedido invalido.";
            return false;
        }

        error = null;
        return true;
    }

    private async Task<OrderState> ReadOrderAsync(BlockRequest request, CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var error))
        {
            return new OrderState(string.Empty, string.Empty, ExternalApiResult.Permanent(error!));
        }

        using var message = Build(request, HttpMethod.Get, "?fields=id,status,owner_note");
        using var client = httpClientFactory.CreateClient(Platform);
        var result = await ExternalApiCaller.SendAsync(client, message, Platform, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return new OrderState(string.Empty, string.Empty, result);
        }

        try
        {
            using var document = JsonDocument.Parse(result.Body ?? "{}");
            var status = document.RootElement.TryGetProperty("status", out var s) ? (s.GetString() ?? string.Empty).ToLowerInvariant() : string.Empty;
            var note = document.RootElement.TryGetProperty("owner_note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? string.Empty : string.Empty;
            return new OrderState(status, note, null);
        }
        catch (JsonException)
        {
            return new OrderState(string.Empty, string.Empty, ExternalApiResult.Permanent("Resposta invalida da Nuvemshop ao ler o pedido."));
        }
    }

    private async Task<ExternalApiResult> WriteNoteAsync(BlockRequest request, string note, CancellationToken cancellationToken)
    {
        using var message = Build(request, HttpMethod.Put, string.Empty);
        message.Content = new StringContent(JsonSerializer.Serialize(new { owner_note = note }), Encoding.UTF8, "application/json");
        using var client = httpClientFactory.CreateClient(Platform);
        return await ExternalApiCaller.SendAsync(client, message, Platform, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage Build(BlockRequest request, HttpMethod method, string suffix)
    {
        var root = options.Value.For(Platform).BaseUrl ?? DefaultBase;
        var message = new HttpRequestMessage(method, new Uri(root, $"v1/{request.Credentials.AccountId}/orders/{request.ExternalOrderId}{suffix}"));
        message.Headers.TryAddWithoutValidation("Authentication", $"bearer {request.Credentials.AccessToken}"); // header nao padrao, conforme a documentacao
        return message;
    }

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex OrderIdPattern();
}
