namespace RuedaLaBocha.Web.Models;

// Bloqueo temporal de un turno hasta cobrar el TOTAL con Padi (demo).
// La reserva NO se crea hasta que el pago queda Succeeded: el hold reserva
// el slot en disponibilidad durante HoldMinutes y al aprobarse genera el
// Booking con precio histórico. Vencido, fallido o cancelado libera el slot
// sin crear reserva. Sin webhooks, sin reembolsos.
public enum BookingHoldStatus
{
    Pending = 0,
    Consumed = 1,
    Released = 2,
}

public sealed class BookingHold
{
    public const int HoldMinutes = 15;

    public int Id { get; set; }

    public int CourtId { get; set; }

    public Court Court { get; set; } = null!;

    public string UserId { get; set; } = "";

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    // Importe total fijado en el servidor (BookingPolicy.PriceArs). La reserva
    // que nazca de este hold hereda este valor como precio histórico.
    public decimal AmountArs { get; set; }

    public string Currency { get; set; } = "ARS";

    public BookingHoldStatus Status { get; set; } = BookingHoldStatus.Pending;

    public DateTime ExpiresAtUtc { get; set; }

    // Id del enlace Padi (POST payment-links -> id). Igual que PadiPayment:
    // la relación pedido<->link vive en nuestra DB.
    public string? PadiLinkId { get; set; }

    public string? PadiPaymentId { get; set; }

    public string? CheckoutUrl { get; set; }

    public string? QrCodeUrl { get; set; }

    public int? BookingId { get; set; }

    public Booking? Booking { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAtUtc;
}
