using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Services;

public sealed class BookingService(ApplicationDbContext db, CourtAvailabilityService availability)
{
    public const string UnknownCourtMessage = "La cancha solicitada no existe.";
    public const string InactiveCourtMessage = "Esta cancha no está disponible para reservas.";
    public const string PastSlotMessage = "No se puede reservar un horario pasado. Elegí una fecha futura.";
    public const string BlockedSlotMessage = "Este horario no está disponible por un bloqueo de la cancha.";
    public const string NoScheduleMessage = "Selecciona una fecha válida.";
    public const string ForbiddenMessage = "Solo podés ver o cancelar tus propias reservas.";
    public const string AlreadyCancelledMessage = "Esta reserva ya estaba cancelada.";

    public async Task<Booking> CreateAsync(string userId, int courtId, DateOnly date, TimeOnly startTime)
    {
        var court = await db.Courts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == courtId);
        if (court is null) throw new BookingNotFoundException(UnknownCourtMessage);
        if (!court.IsActive) throw new BookingValidationException(InactiveCourtMessage);
        if (date < DateOnly.FromDateTime(DateTime.Today)) throw new BookingValidationException(PastSlotMessage);

        if (await db.Bookings.AnyAsync(b =>
                b.CourtId == courtId && b.Date == date &&
                b.StartTime == startTime && b.Status == BookingStatus.Active))
            throw new BookingConflictException(BookingConflictException.SlotTakenMessage);

        var available = await availability.GetAvailableStartTimesAsync(courtId, date);
        if (!available.Contains(startTime))
        {
            var schedules = await db.CourtSchedules.AsNoTracking()
                .AnyAsync(s => s.CourtId == courtId && s.DayOfWeek == date.DayOfWeek);
            if (!schedules) throw new BookingValidationException(NoScheduleMessage);
            throw new BookingValidationException(BlockedSlotMessage);
        }

        var booking = new Booking
        {
            CourtId = courtId,
            UserId = userId,
            Date = date,
            StartTime = startTime,
            EndTime = startTime.Add(TimeSpan.FromMinutes(BookingPolicy.DurationMinutes)),
            PriceArs = BookingPolicy.PriceArs,
            Status = BookingStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new BookingConflictException(BookingConflictException.SlotTakenMessage);
        }
        return booking;
    }

    public Task<List<Booking>> MineAsync(string userId, BookingStatus? status) =>
        QueryMine(userId, status).ToListAsync();

    public Task<List<Booking>> AdminListAsync(BookingFilter filter)
    {
        var query = db.Bookings.AsNoTracking().Include(b => b.Court).AsQueryable();
        if (filter.Date.HasValue) query = query.Where(b => b.Date == filter.Date.Value);
        if (filter.CourtId.HasValue) query = query.Where(b => b.CourtId == filter.CourtId.Value);
        if (filter.Status.HasValue) query = query.Where(b => b.Status == filter.Status.Value);
        if (filter.UserId is not null) query = query.Where(b => b.UserId == filter.UserId);
        return query.OrderBy(b => b.Date).ThenBy(b => b.StartTime).ToListAsync();
    }

    public async Task<Booking?> GetForViewerAsync(int id, string viewerUserId, bool isAdmin)
    {
        var booking = await db.Bookings.AsNoTracking().Include(b => b.Court)
            .SingleOrDefaultAsync(b => b.Id == id);
        if (booking is null) return null;
        if (!isAdmin && booking.UserId != viewerUserId)
            throw new BookingForbiddenException(ForbiddenMessage);
        return booking;
    }

    public async Task<(Booking Booking, bool WasAlreadyCancelled)> CancelAsync(int id, string callerUserId, bool isAdmin)
    {
        var booking = await db.Bookings.Include(b => b.Court).SingleOrDefaultAsync(b => b.Id == id);
        if (booking is null) throw new BookingNotFoundException(UnknownCourtMessage);
        if (!isAdmin && booking.UserId != callerUserId)
            throw new BookingForbiddenException(ForbiddenMessage);
        if (booking.Status == BookingStatus.Cancelled) return (booking, true);
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (booking, false);
    }

    private IQueryable<Booking> QueryMine(string userId, BookingStatus? status)
    {
        var query = db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.UserId == userId);
        if (status.HasValue) query = query.Where(b => b.Status == status.Value);
        return query.OrderBy(b => b.Date).ThenBy(b => b.StartTime);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var text = ex.InnerException?.Message ?? ex.Message;
        // SQLite: "UNIQUE constraint failed: Bookings...". SQL Server equivalent: errors 2601/2627.
        return text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }
}
