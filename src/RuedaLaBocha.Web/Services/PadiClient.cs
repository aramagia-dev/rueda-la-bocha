using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RuedaLaBocha.Web.Services;

// Clave solo desde configuración privada (user-secrets o variable de entorno
// Padi__ApiKey). Nunca commitear ni loguear.
public sealed class PadiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://padi.runasp.net";
    public int TtlMinutes { get; set; } = 30;
}

// POST /api/v1/integrations/payment-links (201). Id es el uuid del ENLACE,
// no del pago: la relación pedido<->enlace vive en nuestra DB.
public sealed record PadiLink(string Id, string CheckoutUrl, string? QrCodeUrl, string Status);

// GET /api/v1/integrations/received-payments/{paymentId}. Id es el uuid del
// PAGO. Solo Succeeded (mismo enlace + importe + moneda) cuenta como cobrado.
public sealed record PadiReceivedPayment(
    string Id,
    string PaymentLinkId,
    decimal Amount,
    string Currency,
    string Status);

public interface IPadiClient
{
    Task<PadiLink> CreateLinkAsync(decimal amountArs, string title, string? description, CancellationToken ct = default);
    Task<IReadOnlyList<PadiReceivedPayment>> ListReceivedPaymentsAsync(int limit = 100, CancellationToken ct = default);
    Task<PadiReceivedPayment?> GetPaymentAsync(string paymentId, CancellationToken ct = default);
}

// Cliente mínimo hand-rolled para https://padi-api.mintlify.site/ (sin SDK).
// Padi no tiene webhooks: la confirmación es solo por polling. Sin clave de
// idempotencia: nunca reintentar el POST automáticamente (ver controlador).
public sealed class PadiClient : IPadiClient
{
    private readonly HttpClient _http;
    private readonly PadiOptions _options;

    public PadiClient(HttpClient http, IOptions<PadiOptions> options)
    {
        _http = http;
        _options = options.Value;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri("https://padi.runasp.net/");
    }

    public async Task<PadiLink> CreateLinkAsync(decimal amountArs, string title, string? description, CancellationToken ct = default)
    {
        var payload = new
        {
            amount = Math.Round(amountArs, 2),
            title,
            currency = "ARS",
            description,
            type = "SingleUse",
            ttlMinutes = _options.TtlMinutes
        };

        using var response = await _http.PostAsJsonAsync("api/v1/integrations/payment-links", payload, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        return new PadiLink(
            GetString(root, "id") ?? string.Empty,
            GetString(root, "checkoutUrl") ?? string.Empty,
            GetString(root, "qrCodeUrl"),
            GetString(root, "status") ?? string.Empty);
    }

    public async Task<IReadOnlyList<PadiReceivedPayment>> ListReceivedPaymentsAsync(int limit = 100, CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 100);
        var list = await _http.GetFromJsonAsync<List<JsonElement>>(
            $"api/v1/integrations/received-payments?limit={clamped}", ct);
        if (list is null)
            return Array.Empty<PadiReceivedPayment>();

        return list.Select(e => new PadiReceivedPayment(
            GetString(e, "id") ?? string.Empty,
            GetString(e, "paymentLinkId") ?? string.Empty,
            GetDecimal(e, "amount"),
            GetString(e, "currency") ?? "ARS",
            GetString(e, "status") ?? string.Empty)).ToList();
    }

    public async Task<PadiReceivedPayment?> GetPaymentAsync(string paymentId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/v1/integrations/received-payments/{paymentId}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        return new PadiReceivedPayment(
            GetString(root, "id") ?? paymentId,
            GetString(root, "paymentLinkId") ?? string.Empty,
            GetDecimal(root, "amount"),
            GetString(root, "currency") ?? "ARS",
            GetString(root, "status") ?? string.Empty);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal GetDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : 0m;
}
