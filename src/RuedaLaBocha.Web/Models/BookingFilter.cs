namespace RuedaLaBocha.Web.Models;

public sealed record BookingFilter(
    DateOnly? Date = null,
    int? CourtId = null,
    BookingStatus? Status = null,
    string? UserId = null);
