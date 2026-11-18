using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[Authorize]
[Route("Bookings")]
public class BookingsController(
    BookingService bookings,
    CourtService courts,
    CourtAvailabilityService availability,
    IPromoService promos) : Controller
{
    [HttpGet("Create")]
    public IActionResult Create(int? courtId, string? date, string? startTime, int hours = 1)
    {
        ViewData["CourtId"] = courtId;
        ViewData["Date"] = date;
        ViewData["StartTime"] = startTime;
        ViewData["Hours"] = Math.Clamp(hours, 1, 3);
        return View();
    }

    [HttpPost("")]
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int courtId, string? date, string? startTime, int hours = 1)
    {
        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(startTime, out var parsedStart))
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        if (hours is < 1 or > 3)
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        try
        {
            if (hours == 1)
                await bookings.CreateAsync(CurrentUserId(), courtId, parsedDate, parsedStart);
            else
                await bookings.CreateManyAsync(CurrentUserId(), courtId, parsedDate, parsedStart, hours);
        }
        catch (BookingNotFoundException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(404);
        }
        catch (BookingValidationException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        }
        catch (BookingConflictException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(409);
        }
        return RedirectToAction(nameof(Mine));
    }

    [HttpGet("Mine")]
    public async Task<IActionResult> Mine(string? status)
    {
        BookingStatus? parsed = null;
        if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: true, out BookingStatus parsedStatus))
                return BadRequest("Selecciona una fecha válida.");
            parsed = parsedStatus;
        }
        return View(await bookings.MineAsync(CurrentUserId(), parsed));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        Booking? booking;
        try
        {
            booking = await bookings.GetForViewerAsync(id, CurrentUserId(), User.IsInRole(AppRoles.Administrator));
        }
        catch (BookingForbiddenException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(403);
        }
        if (booking is null) return NotFound();
        return View(booking);
    }

    [HttpPost("{id:int}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            var (_, wasAlreadyCancelled) = await bookings.CancelAsync(id, CurrentUserId(), User.IsInRole(AppRoles.Administrator));
            if (wasAlreadyCancelled)
                return Content(BookingService.AlreadyCancelledMessage, "text/plain", System.Text.Encoding.UTF8).WithStatus(200);
        }
        catch (BookingNotFoundException)
        {
            return NotFound();
        }
        catch (BookingForbiddenException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(403);
        }
        TempData["BookingMessage"] = "Tu reserva fue cancelada. El horario quedó libre nuevamente.";
        return RedirectToAction(nameof(Mine));
    }

    // Detail before payment: shows court, date, chosen contiguous slots and
    // total with promo applied. Does NOT create bookings; Confirm POST does.
    [HttpGet("Summary")]
    public async Task<IActionResult> Summary(int courtId, string? date, string? startTime, int hours = 1)
    {
        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(startTime, out var parsedStart))
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        if (hours is < 1 or > 3)
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        var court = await courts.FindAsync(courtId);
        if (court is null || !court.IsActive) return NotFound();

        var duration = TimeSpan.FromMinutes(BookingPolicy.DurationMinutes);
        var requested = Enumerable.Range(0, hours)
            .Select(offset => parsedStart.Add(duration * offset))
            .ToList();
        for (var i = 1; i < requested.Count; i++)
        {
            if (requested[i] != requested[i - 1].Add(duration))
                return Content("Los horarios elegidos deben estar libres y ser contiguos.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        }

        var available = await availability.GetAvailableStartTimesAsync(courtId, parsedDate);
        if (!requested.All(available.Contains))
            return Content("Los horarios elegidos deben estar libres y ser contiguos.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);

        var total = await promos.GetTotalPriceAsync(parsedDate, hours);
        var ends = requested.Select(start => start.Add(duration)).ToList();
        return View(new BookingSummaryViewModel(court, parsedDate, requested, ends, hours, total,
            IsPromo: hours == PromoService.PromoHours && PromoService.IsPromoDayOfWeek(parsedDate.DayOfWeek)));
    }

    // Creates the reservations and hands off to Padi checkout with the total.
    // Single slot -> reservaId is the booking Id; group -> GroupId string.
    [HttpPost("Confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int courtId, string? date, string? startTime, int hours = 1)
    {
        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(startTime, out var parsedStart))
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        if (hours is < 1 or > 3)
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        IReadOnlyList<Booking> created;
        try
        {
            created = hours == 1
                ? [await bookings.CreateAsync(CurrentUserId(), courtId, parsedDate, parsedStart)]
                : await bookings.CreateManyAsync(CurrentUserId(), courtId, parsedDate, parsedStart, hours);
        }
        catch (BookingNotFoundException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(404);
        }
        catch (BookingValidationException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        }
        catch (BookingConflictException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(409);
        }
        var groupKey = created.Count > 1 && created[0].GroupId.HasValue
            ? created[0].GroupId!.Value.ToString()
            : created[0].Id.ToString();
        var total = created.Sum(b => b.PriceArs);
        return RedirectToAction("Checkout", "Payments", new { reservaId = groupKey, amount = total });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
}

file static class ContentResultExtensions
{
    public static ContentResult WithStatus(this ContentResult result, int statusCode)
    {
        result.StatusCode = statusCode;
        return result;
    }
}
