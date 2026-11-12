namespace RuedaLaBocha.Web.Models;

public sealed record DashboardCourtRow(
    string Name,
    int DayBookings,
    decimal DayRevenue);

public sealed record DashboardDayBooking(
    int Id,
    string CourtName,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string UserId,
    decimal PriceArs,
    BookingStatus Status);

public sealed record DashboardViewModel(
    DateOnly Selected,
    decimal DayRevenue,
    decimal MonthRevenue,
    decimal YearRevenue,
    int OccupiedSlots,
    int OfferedSlots,
    int OccupancyPercent,
    int PendingHolds,
    int CancelledBookings,
    IReadOnlyList<DashboardCourtRow> ByCourt,
    IReadOnlyList<DashboardDayBooking> DayBookings,
    IReadOnlyList<Booking> ActiveBookings,
    string? Notice);
