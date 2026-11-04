using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RiskGuard.IntegrationTests.Infrastructure;

public sealed record PlatformCall(string Platform, string Method, Uri Url, string? Token, string? Body)
{
    public override string ToString() => $"{Platform} {Method} {Url.PathAndQuery}";
}

/// <summary>
/// Shopify e Nuvemshop FALSAS e com estado, no formato das APIs reais: a Shopify mantem fulfillment orders (open/on_hold) e a
/// Nuvemshop mantem pedidos com nota interna. Substitui a rede do HttpClientFactory nos testes de integracao.
/// </summary>
public sealed partial class FakePlatformsHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<PlatformCall> _calls = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<HttpStatusCode>> _scripts = new();
    private readonly ConcurrentDictionary<string, List<FulfillmentOrder>> _shopifyOrders = new();
    private readonly ConcurrentDictionary<string, NuvemshopOrder> _nuvemshopOrders = new();
    private long _nextFulfillmentOrderId = 7_000_000_000;

    public IReadOnlyCollection<PlatformCall> Calls => _calls;

    public IReadOnlyList<PlatformCall> CallsFor(string externalOrderId) =>
        _calls.Where(c => c.Url.AbsolutePath.Contains($"/orders/{externalOrderId}", StringComparison.Ordinal)
                          || _shopifyOrders.TryGetValue(externalOrderId, out var fos)
                             && fos.Any(f => c.Url.AbsolutePath.Contains($"/fulfillment_orders/{f.Id}/", StringComparison.Ordinal))).ToList();

    /// <summary>Respostas (por ordem) para as proximas chamadas deste pedido, antes de aplicar o comportamento normal.</summary>
    public void Script(string externalOrderId, params HttpStatusCode[] statuses) =>
        _scripts[externalOrderId] = new ConcurrentQueue<HttpStatusCode>(statuses);

    // ---- Shopify ----

    /// <summary>Statuses atuais das fulfillment orders do pedido (cria uma <c>open</c> se ainda nao existir).</summary>
    public IReadOnlyList<string> ShopifyStatuses(string orderId) => ShopifyOf(orderId).Select(f => f.Status).ToList();

    public IReadOnlyList<string> ShopifyHoldNotes(string orderId) => ShopifyOf(orderId).SelectMany(f => f.Holds).ToList();

    /// <summary>Simula um hold colocado pelo proprio lojista na Shopify.</summary>
    public void AddMerchantHold(string orderId, string note)
    {
        foreach (var fo in ShopifyOf(orderId))
        {
            fo.Status = "on_hold";
            fo.Holds.Add(note);
        }
    }

    // ---- Nuvemshop ----

    public void AddNuvemshopOrder(string orderId, string email, string total, string createdAt = "2026-03-01T09:11:51+0000", string status = "open", string? note = null) =>
        _nuvemshopOrders[orderId] = new NuvemshopOrder(orderId, email, total, createdAt) { Status = status, Note = note };

    public string? NuvemshopNote(string orderId) => _nuvemshopOrders.TryGetValue(orderId, out var o) ? o.Note : null;

    // ---- roteamento ----

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var platform = url.Host.StartsWith("shopify", StringComparison.Ordinal) ? "shopify" : "nuvemshop";
        request.Headers.TryGetValues(platform == "shopify" ? "X-Shopify-Access-Token" : "Authentication", out var tokens);
        _calls.Enqueue(new PlatformCall(platform, request.Method.Method, url, tokens?.FirstOrDefault(), body));

        return platform == "shopify" ? HandleShopify(request.Method, url, body) : HandleNuvemshop(request.Method, url, body);
    }

    private HttpResponseMessage HandleShopify(HttpMethod method, Uri url, string? body)
    {
        var path = url.AbsolutePath;

        if (method == HttpMethod.Get && ListPath().Match(path) is { Success: true } list)
        {
            var orderId = list.Groups["order"].Value;
            if (Scripted(orderId) is { } scripted)
            {
                return new HttpResponseMessage(scripted);
            }

            var items = ShopifyOf(orderId).Select(f => new
            {
                id = f.Id,
                order_id = long.Parse(orderId, System.Globalization.CultureInfo.InvariantCulture),
                status = f.Status,
                fulfillment_holds = f.Holds.Select(n => new { reason = "other", reason_notes = n }),
            });
            return Json(HttpStatusCode.OK, new { fulfillment_orders = items });
        }

        if (method == HttpMethod.Post && HoldPath().Match(path) is { Success: true } hold)
        {
            var (orderId, fo) = FindFulfillmentOrder(hold.Groups["fo"].Value);
            if (fo is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (Scripted(orderId!) is { } scripted)
            {
                return new HttpResponseMessage(scripted);
            }

            if (fo.Status != "open" && fo.Status != "scheduled")
            {
                return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity);
            }

            using var document = JsonDocument.Parse(body ?? "{}");
            var notes = document.RootElement.GetProperty("fulfillment_hold").TryGetProperty("reason_notes", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            fo.Status = "on_hold";
            fo.Holds.Add(notes);
            return Json(HttpStatusCode.OK, new { fulfillment_order = new { id = fo.Id, status = fo.Status } });
        }

        if (method == HttpMethod.Post && ReleasePath().Match(path) is { Success: true } release)
        {
            var (orderId, fo) = FindFulfillmentOrder(release.Groups["fo"].Value);
            if (fo is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (Scripted(orderId!) is { } scripted)
            {
                return new HttpResponseMessage(scripted);
            }

            fo.Status = "open";
            fo.Holds.Clear();
            return Json(HttpStatusCode.OK, new { fulfillment_order = new { id = fo.Id, status = fo.Status } });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage HandleNuvemshop(HttpMethod method, Uri url, string? body)
    {
        if (NuvemshopOrderPath().Match(url.AbsolutePath) is not { Success: true } match)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var orderId = match.Groups["order"].Value;
        if (Scripted(orderId) is { } scripted)
        {
            return new HttpResponseMessage(scripted);
        }

        if (!_nuvemshopOrders.TryGetValue(orderId, out var order))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (method == HttpMethod.Get)
        {
            // Com ?fields=... a API devolve so esses campos; sem fields, o pedido completo.
            return url.Query.Contains("fields=", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, new { id = long.Parse(orderId, System.Globalization.CultureInfo.InvariantCulture), status = order.Status, owner_note = order.Note })
                : Json(HttpStatusCode.OK, new
                {
                    id = long.Parse(orderId, System.Globalization.CultureInfo.InvariantCulture),
                    contact_email = order.Email,
                    total = order.Total,
                    created_at = order.CreatedAt,
                    status = order.Status,
                    owner_note = order.Note,
                });
        }

        if (method == HttpMethod.Put)
        {
            using var document = JsonDocument.Parse(body ?? "{}");
            order.Note = document.RootElement.TryGetProperty("owner_note", out var n) ? n.GetString() : order.Note;
            return Json(HttpStatusCode.OK, new { id = long.Parse(orderId, System.Globalization.CultureInfo.InvariantCulture), owner_note = order.Note });
        }

        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
    }

    private List<FulfillmentOrder> ShopifyOf(string orderId) =>
        _shopifyOrders.GetOrAdd(orderId, _ => [new FulfillmentOrder(Interlocked.Increment(ref _nextFulfillmentOrderId)) { Status = "open" }]);

    private (string? OrderId, FulfillmentOrder? Order) FindFulfillmentOrder(string fulfillmentOrderId)
    {
        foreach (var (orderId, list) in _shopifyOrders)
        {
            var found = list.FirstOrDefault(f => f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == fulfillmentOrderId);
            if (found is not null)
            {
                return (orderId, found);
            }
        }

        return (null, null);
    }

    private HttpStatusCode? Scripted(string orderId) =>
        _scripts.TryGetValue(orderId, out var queue) && queue.TryDequeue(out var status) ? status : null;

    private static HttpResponseMessage Json(HttpStatusCode status, object value) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private sealed class FulfillmentOrder(long id)
    {
        public long Id { get; } = id;

        public string Status { get; set; } = "open";

        public List<string> Holds { get; } = [];
    }

    private sealed class NuvemshopOrder(string id, string email, string total, string createdAt)
    {
        public string Id { get; } = id;

        public string Email { get; } = email;

        public string Total { get; } = total;

        public string CreatedAt { get; } = createdAt;

        public string Status { get; set; } = "open";

        public string? Note { get; set; }
    }

    [GeneratedRegex(@"/admin/api/[^/]+/orders/(?<order>\d+)/fulfillment_orders\.json$")]
    private static partial Regex ListPath();

    [GeneratedRegex(@"/admin/api/[^/]+/fulfillment_orders/(?<fo>\d+)/hold\.json$")]
    private static partial Regex HoldPath();

    [GeneratedRegex(@"/admin/api/[^/]+/fulfillment_orders/(?<fo>\d+)/release_hold\.json$")]
    private static partial Regex ReleasePath();

    [GeneratedRegex(@"/v1/\d+/orders/(?<order>\d+)$")]
    private static partial Regex NuvemshopOrderPath();
}
