namespace RuedaLaBocha.Web.Services;

// DTOs for the Padi integrations API (https://padi-api.mintlify.site/).
// Paths already contain /api/v1/; amounts are ARS with 2 decimals.

public sealed record PaymentLinkRequest(
    decimal Amount,
    string Title,
    string? Description = null,
    string Type = "SingleUse",
    int? TtlMinutes = null);

// POST /api/v1/integrations/payment-links (201). Note: Id is the LINK uuid,
// NOT the payment id — the order<->link mapping lives in our DB.
public sealed record PaymentLink(
    string Id,
    string Token,
    string CheckoutUrl,
    string? QrCodeUrl,
    string Status,
    DateTimeOffset? ExpiresAt);

public sealed record PaymentLinkStatus(
    string Id,
    string Status,
    string? Token = null,
    string? CheckoutUrl = null,
    DateTimeOffset? ExpiresAt = null);

// GET /api/v1/integrations/received-payments/{paymentId}. Id is the PAYMENT
// uuid. ONLY Succeeded means paid.
public sealed record ReceivedPayment(
    string Id,
    string PaymentLinkId,
    decimal Amount,
    string Currency,
    string Status,
    string? FailureReason,
    string RawJson);

public interface IPaymentGateway
{
    Task<PaymentLink> CreateLinkAsync(PaymentLinkRequest request, CancellationToken ct = default);
    Task<PaymentLinkStatus?> GetLinkAsync(string linkId, CancellationToken ct = default);
    Task<IReadOnlyList<ReceivedPayment>> ListReceivedPaymentsAsync(int limit = 100, CancellationToken ct = default);
    Task<ReceivedPayment?> GetPaymentAsync(string paymentId, CancellationToken ct = default);

    // Reconciles one local payment: lists recent payments, filters by link id,
    // and confirms the match with GET by payment id (source of truth).
    // Returns the confirmed payment, or null when nothing is found for the link.
    Task<ReceivedPayment?> FindPaymentsByLinkAsync(string linkId, CancellationToken ct = default);
}
