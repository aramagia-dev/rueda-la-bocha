using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RuedaLaBocha.Web.Services;

// Hand-rolled HttpClient for the Padi integrations API
// (https://padi-api.mintlify.site/): there is no NuGet/C# SDK.
// Auth is server-side only via the X-Api-Key header — the key is never logged.
// Padi has NO webhooks, so confirmation is poll-only (see PaymentsController).
public sealed class PadiClient : IPaymentGateway
{
    private readonly HttpClient _http;
    private readonly PadiOptions _options;
    private readonly ILogger<PadiClient> _logger;

    public PadiClient(
        HttpClient http,
        IOptions<PadiOptions> options,
        ILogger<PadiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri("https://padi.runasp.net/");
    }

    public async Task<PaymentLink> CreateLinkAsync(PaymentLinkRequest request, CancellationToken ct = default)
    {
        // No Idempotency-Key support: retrying this POST creates a SECOND link.
        // Callers must never auto-retry; reconcile via the Cobros screen instead.
        var payload = new
        {
            amount = Math.Round(request.Amount, 2),
            title = request.Title,
            currency = "ARS",
            description = request.Description,
            type = request.Type,
            ttlMinutes = request.TtlMinutes ?? _options.TtlMinutes
        };

        using var response = await _http.PostAsJsonAsync("api/v1/integrations/payment-links", payload, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        return new PaymentLink(
            GetString(root, "id") ?? string.Empty,
            GetString(root, "token") ?? string.Empty,
            GetString(root, "checkoutUrl") ?? string.Empty,
            GetString(root, "qrCodeUrl"),
            GetString(root, "status") ?? string.Empty,
            GetDate(root, "expiresAt"));
    }

    public async Task<PaymentLinkStatus?> GetLinkAsync(string linkId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/v1/integrations/payment-links/{linkId}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        return new PaymentLinkStatus(
            GetString(root, "id") ?? linkId,
            GetString(root, "status") ?? string.Empty,
            GetString(root, "token"),
            GetString(root, "checkoutUrl"),
            GetDate(root, "expiresAt"));
    }

    public async Task<IReadOnlyList<ReceivedPayment>> ListReceivedPaymentsAsync(int limit = 100, CancellationToken ct = default)
    {
        // Newest-first, no pagination; limit is clamped server-side to 1-100.
        var clamped = Math.Clamp(limit, 1, 100);
        var list = await _http.GetFromJsonAsync<List<JsonElement>>(
            $"api/v1/integrations/received-payments?limit={clamped}", ct);
        if (list is null)
            return Array.Empty<ReceivedPayment>();

        return list.Select(e => new ReceivedPayment(
            GetString(e, "id") ?? string.Empty,
            GetString(e, "paymentLinkId") ?? string.Empty,
            GetDecimal(e, "amount"),
            GetString(e, "currency") ?? "ARS",
            GetString(e, "status") ?? string.Empty,
            GetString(e, "failureReason"),
            e.GetRawText())).ToList();
    }

    public async Task<ReceivedPayment?> GetPaymentAsync(string paymentId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/v1/integrations/received-payments/{paymentId}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var raw = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        return new ReceivedPayment(
            GetString(root, "id") ?? paymentId,
            GetString(root, "paymentLinkId") ?? string.Empty,
            GetDecimal(root, "amount"),
            GetString(root, "currency") ?? "ARS",
            GetString(root, "status") ?? string.Empty,
            GetString(root, "failureReason"),
            raw);
    }

    public async Task<ReceivedPayment?> FindPaymentsByLinkAsync(string linkId, CancellationToken ct = default)
    {
        var matches = (await ListReceivedPaymentsAsync(100, ct))
            .Where(p => string.Equals(p.PaymentLinkId, linkId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
            return null;
        if (matches.Count > 1)
            _logger.LogWarning(
                "Found {Count} received payments for SingleUse link {LinkId}; confirming each",
                matches.Count, linkId);

        foreach (var match in matches)
        {
            var confirmed = await GetPaymentAsync(match.Id, ct);
            if (confirmed is not null
                && string.Equals(confirmed.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                return confirmed;
        }

        // Nothing succeeded yet: return the confirmed first match so the caller
        // can still display Processing/Pending state.
        return await GetPaymentAsync(matches[0].Id, ct);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal GetDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : 0m;

    private static DateTimeOffset? GetDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out var date)
            ? date
            : null;
}
