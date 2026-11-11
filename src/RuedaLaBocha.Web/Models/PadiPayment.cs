using System.ComponentModel.DataAnnotations;

namespace RuedaLaBocha.Web.Models;

// Enlace de pago Padi (sandbox, demo universitaria) atado a una reserva.
// El importe se fija en el servidor desde Booking.PriceArs; el enlace y el
// QR son solo display. Solo Succeeded (mismo enlace + importe + moneda)
// cuenta como aprobado; el resto queda pendiente o terminal sin aprobar.
public enum PadiPaymentStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

public sealed class PadiPayment
{
    public int Id { get; set; }

    public int BookingId { get; set; }

    public Booking Booking { get; set; } = null!;

    // Id del enlace Padi (POST payment-links -> id). La relación pedido<->link
    // vive en nuestra DB: Padi no envía webhooks ni external_reference.
    [MaxLength(64)]
    public string? PadiLinkId { get; set; }

    // Id del pago Padi (GET received-payments/{id} -> id). Solo una fila con
    // este campo y Status == Succeeded cuenta como pagada.
    [MaxLength(64)]
    public string? PadiPaymentId { get; set; }

    [MaxLength(512)]
    public string? CheckoutUrl { get; set; }

    [MaxLength(512)]
    public string? QrCodeUrl { get; set; }

    public decimal AmountArs { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "ARS";

    public PadiPaymentStatus Status { get; set; } = PadiPaymentStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
