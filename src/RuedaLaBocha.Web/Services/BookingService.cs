using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Services;

public sealed class BookingService(ApplicationDbContext db, CourtAvailabilityService availability, IPromoService promos)
{
    public const string UnknownCourtMessage = "La cancha solicitada no existe.";
    public const string InactiveCourtMessage = "Esta cancha no está disponible para reservas.";
    public const string PastSlotMessage = "No se puede reservar un horario pasado. Elegí una fecha futura.";
    public const string BlockedSlotMessage = "Este horario no está disponible por un bloqueo de la cancha.";
    public const string NoScheduleMessage = "Selecciona una fecha válida.";
    public const string ForbiddenMessage = "Solo podés ver o cancelar tus propias reservas.";
    public const string AlreadyCancelledMessage = "Esta reserva ya estaba cancelada.";

    public Task<Booking> CreateAsync(string userId, int courtId, DateOnly date, TimeOnly startTime) =>
        CreateSingleAsync(userId, courtId, date, startTime);

    public async Task<IReadOnlyList<Booking>> CreateManyAsync(string userId, int courtId, DateOnly date, TimeOnly startTime, int slotCount)
    {
        if (slotCount < 1)
            throw new BookingValidationException(NoScheduleMessage);

        var court = await db.Courts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == courtId);
        if (court is null) throw new BookingNotFoundException(UnknownCourtMessage);
        if (!court.IsActive) throw new BookingValidationException(InactiveCourtMessage);
        if (date < DateOnly.FromDateTime(DateTime.Today)) throw new BookingValidationException(PastSlotMessage);

        var duration = TimeSpan.FromMinutes(BookingPolicy.DurationMinutes);
        var requested = Enumerable.Range(0, slotCount)
            .Select(offset => startTime.Add(duration * offset))
            .ToList();

        if (await db.Bookings.AnyAsync(b =>
                b.CourtId == courtId && b.Date == date &&
                requested.Contains(b.StartTime) && b.Status == BookingStatus.Active))
            throw new BookingConflictException(BookingConflictException.SlotTakenMessage);

        var available = await availability.GetAvailableStartTimesAsync(courtId, date);
        if (!requested.All(available.Contains))
        {
            var schedules = await db.CourtSchedules.AsNoTracking()
                .AnyAsync(s => s.CourtId == courtId && s.DayOfWeek == date.DayOfWeek);
            if (!schedules) throw new BookingValidationException(NoScheduleMessage);
            throw new BookingValidationException(BlockedSlotMessage);
        }

        var total = await promos.GetTotalPriceAsync(date, slotCount);
        var perSlot = total / slotCount;
        var groupId = slotCount > 1 ? Guid.NewGuid() : (Guid?)null;
        var now = DateTime.UtcNow;
        var bookings = requested.Select(start => new Booking
        {
            CourtId = courtId,
            UserId = userId,
            Date = date,
            StartTime = start,
            EndTime = start.Add(duration),
            PriceArs = perSlot,
            Status = BookingStatus.Active,
            GroupId = groupId,
            CreatedAtUtc = now,
        }).ToList();

        db.Bookings.AddRange(bookings);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new BookingConflictException(BookingConflictException.SlotTakenMessage);
        }
        return bookings;
    }

    private async Task<Booking> CreateSingleAsync(string userId, int courtId, DateOnly date, TimeOnly startTime) =>
        (await CreateManyAsync(userId, courtId, date, startTime, 1)).Single();

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
