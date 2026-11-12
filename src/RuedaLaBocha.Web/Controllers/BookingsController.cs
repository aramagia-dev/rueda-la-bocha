using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[Authorize]
[Route("Bookings")]
public class BookingsController(BookingService bookings, CourtService courts, CourtAvailabilityService availability, ApplicationDbContext db) : Controller
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(int? courtId, string? date, string? startTime)
    {
        if (courtId is null && date is null && startTime is null) return View();
        if (!courtId.HasValue || !DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(startTime, out var parsedStart))
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        var court = await courts.FindAsync(courtId.Value);
        if (court is null) return Content(BookingService.UnknownCourtMessage, "text/plain", System.Text.Encoding.UTF8).WithStatus(404);
        if (!court.IsActive) return Content(BookingService.InactiveCourtMessage, "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        if (parsedDate < DateOnly.FromDateTime(DateTime.Today))
            return Content(BookingService.PastSlotMessage, "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        if (!(await availability.GetAvailableStartTimesAsync(courtId.Value, parsedDate)).Contains(parsedStart))
            return Content(BookingService.BlockedSlotMessage, "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        ViewData["Court"] = court;
        ViewData["Date"] = parsedDate;
        ViewData["StartTime"] = parsedStart;
        return View();
    }

    [HttpPost("")]
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create()
    {
        // La reserva solo nace con pago total (holds + Padi): el POST
        // directo se rechaza sin crear nada.
        return Content("La reserva se crea solo con pago total; usá el botón Pagar total con Padi (demo).", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
    }

    [HttpGet("Mine")]
    public async Task<IActionResult> Mine(string? status)
    {
        var now = DateTime.UtcNow;
        var userId = CurrentUserId();
        // B4: holds propios Pending aún vigentes, ordenados por vencimiento.
        // No toca BookingService ni la expiración/Status.
        ViewBag.PendingHolds = await db.BookingHolds.AsNoTracking().Include(h => h.Court)
            .Where(h => h.UserId == userId && h.Status == BookingHoldStatus.Pending && h.ExpiresAtUtc > now)
            .OrderBy(h => h.ExpiresAtUtc).ToListAsync();
        if (string.Equals(status, nameof(BookingHoldStatus.Pending), StringComparison.OrdinalIgnoreCase))
        {
            ViewData["Status"] = nameof(BookingHoldStatus.Pending);
            return View(new List<Booking>());
        }
        BookingStatus? parsed = null;
        if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: true, out BookingStatus parsedStatus))
                return BadRequest("Selecciona una fecha válida.");
            parsed = parsedStatus;
        }
        ViewData["Status"] = status;
        return View(await bookings.MineAsync(userId, parsed));
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
        // Origen de la reserva: el hold consumido que la creó al aprobarse
        // el pago total. Sin hold es legacy directa (sin pago asociado).
        ViewBag.Hold = await db.BookingHolds.AsNoTracking()
            .FirstOrDefaultAsync(h => h.BookingId == id && h.Status == BookingHoldStatus.Consumed);
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
