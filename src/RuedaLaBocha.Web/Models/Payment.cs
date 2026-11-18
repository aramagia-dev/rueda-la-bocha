using System.ComponentModel.DataAnnotations;

namespace RuedaLaBocha.Web.Models;

// Deposit ("sena") payment linked to a reservation.
// ReservaId is a placeholder foreign key: Franco owns the Reservation
// entity, so this stays a plain string until that model exists.
// The order<->link mapping lives in OUR db: Padi sends no webhooks and no
// external_reference, so PaymentLinkId is how a local row finds its remote
// link, and PadiPaymentId (the payment uuid from received-payments) is how
// a confirmed payment is matched exactly once.
public class Payment
{
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string ReservaId { get; set; } = string.Empty;

    // Padi payment uuid (GET received-payments/{paymentId} -> id).
    // ONLY a row with this set and Status == Approved counts as paid.
    [MaxLength(64)]
    public string? PadiPaymentId { get; set; }

    // Padi payment-link uuid (POST payment-links -> id). Display only:
    // CheckoutUrl is where the buyer pays, QrCodeUrl its SVG image.
    [MaxLength(64)]
    public string? PaymentLinkId { get; set; }

    [MaxLength(512)]
    public string? CheckoutUrl { get; set; }

    [MaxLength(512)]
    public string? QrCodeUrl { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    // Raw Padi GET received-payments/{paymentId} payload (audit trail).
    public string? RawJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
