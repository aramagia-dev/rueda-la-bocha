namespace RuedaLaBocha.Web.Models;

public sealed class Booking
{
    public int Id { get; set; }

    public int CourtId { get; set; }

    public Court Court { get; set; } = null!;

    public string UserId { get; set; } = "";

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public decimal PriceArs { get; set; }

    public BookingStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
}
