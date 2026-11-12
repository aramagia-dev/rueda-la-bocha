using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
[Route("Admin/Dashboard")]
public class AdminDashboardController(
    ApplicationDbContext db,
    CourtService courts,
    CourtAvailabilityService availability) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? date)
    {
        var today = TodayInBuenosAires(DateTime.UtcNow);
        string? notice = null;
        DateOnly selected;
        if (date is null)
        {
            selected = today;
        }
        else if (!DateOnly.TryParse(date, out var parsed))
        {
            selected = today;
            notice = "Fecha inválida, se muestra hoy.";
        }
        else
        {
            selected = parsed;
        }

        var active = await db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.Status == BookingStatus.Active)
            .ToListAsync();
        var day = active.Where(b => b.Date == selected).ToList();

        var now = DateTime.UtcNow;
        var pendingHolds = await db.BookingHolds.AsNoTracking()
            .CountAsync(h => h.Status == BookingHoldStatus.Pending && h.ExpiresAtUtc > now);
        var cancelled = await db.Bookings.AsNoTracking()
            .CountAsync(b => b.Status == BookingStatus.Cancelled);

        var courtsById = (await courts.ListAsync(includeInactive: true))
            .ToDictionary(c => c.Id, c => c.Name);
        var courtIds = await courts.ListAsync();
        int occupied = 0, offered = 0;
        foreach (var court in courtIds)
        {
            var taken = day.Count(b => b.CourtId == court.Id);
            var free = (await availability.GetAvailableStartTimesAsync(court.Id, selected)).Count;
            occupied += taken;
            offered += taken + free;
        }

        var dayBookings = await db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.Date == selected)
            .OrderBy(b => b.StartTime)
            .ToListAsync();

        return View("~/Views/Dashboard/Index.cshtml", new DashboardViewModel(
            selected,
            day.Sum(b => b.PriceArs),
            active.Where(b => b.Date.Year == selected.Year && b.Date.Month == selected.Month).Sum(b => b.PriceArs),
            active.Where(b => b.Date.Year == selected.Year).Sum(b => b.PriceArs),
            occupied,
            offered,
            offered == 0 ? 0 : occupied * 100 / offered,
            pendingHolds,
            cancelled,
            courtIds.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new DashboardCourtRow(
                    c.Name,
                    day.Count(b => b.CourtId == c.Id),
                    day.Where(b => b.CourtId == c.Id).Sum(b => b.PriceArs)))
                .ToList(),
            dayBookings.Select(b => new DashboardDayBooking(
                b.Id,
                courtsById.TryGetValue(b.CourtId, out var name) ? name : b.Court.Name,
                b.StartTime, b.EndTime, b.UserId, b.PriceArs, b.Status)).ToList(),
            active.OrderBy(b => b.Date).ThenBy(b => b.StartTime).ToList(),
            notice));
    }

    internal static DateOnly TodayInBuenosAires(DateTime utcNow)
    {
        foreach (var id in new[] { "America/Argentina_Buenos_Aires", "Argentina Standard Time" })
        {
            try
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    utcNow, TimeZoneInfo.FindSystemTimeZoneById(id)));
            }
            catch (TimeZoneNotFoundException) { }
        }
        return DateOnly.FromDateTime(utcNow);
    }
}
