using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Services;

public sealed class CourtAvailabilityService(ApplicationDbContext db) : ICourtAvailabilityService
{
    public async Task<List<TimeOnly>> GetAvailableStartTimesAsync(int courtId, DateOnly date)
    {
        var active = await db.Courts.AsNoTracking()
            .AnyAsync(court => court.Id == courtId && court.IsActive);
        if (!active) return [];

        var schedules = await db.CourtSchedules.AsNoTracking()
            .Where(schedule => schedule.CourtId == courtId && schedule.DayOfWeek == date.DayOfWeek)
            .OrderBy(schedule => schedule.StartTime)
            .ToListAsync();
        if (schedules.Count == 0) return [];

        var closures = await db.CourtClosures.AsNoTracking()
            .Where(closure => closure.CourtId == courtId && closure.Date == date)
            .ToListAsync();

        var taken = await db.Bookings.AsNoTracking()
            .Where(b => b.CourtId == courtId && b.Date == date && b.Status == BookingStatus.Active)
            .Select(b => b.StartTime)
            .ToListAsync();
        var takenSet = new HashSet<TimeOnly>(taken);

        // Holds vigentes (no vencidos ni consumidos/liberados) bloquean el slot
        // 15 minutos hasta cobrarse el total. Vencidos o terminales liberan.
        var now = DateTime.UtcNow;
        var held = await db.BookingHolds.AsNoTracking()
            .Where(h => h.CourtId == courtId && h.Date == date &&
                h.Status == BookingHoldStatus.Pending && h.ExpiresAtUtc > now)
            .Select(h => h.StartTime)
            .ToListAsync();
        var heldSet = new HashSet<TimeOnly>(held);

        var duration = TimeSpan.FromMinutes(BookingPolicy.DurationMinutes);
        var slots = new List<TimeOnly>();
        foreach (var schedule in schedules)
        {
            var closingTime = schedule.EndTime.ToTimeSpan();
            for (var start = schedule.StartTime.ToTimeSpan(); start + duration <= closingTime; start += duration)
            {
                var end = start + duration;
                var slotStart = TimeOnly.FromTimeSpan(start);
                var slotEnd = TimeOnly.FromTimeSpan(end);
                var blocked = closures.Any(closure => closure.StartTime < slotEnd && closure.EndTime > slotStart);
                if (!blocked && !takenSet.Contains(slotStart) && !heldSet.Contains(slotStart)) slots.Add(slotStart);
            }
        }

        return slots;
    }
}
