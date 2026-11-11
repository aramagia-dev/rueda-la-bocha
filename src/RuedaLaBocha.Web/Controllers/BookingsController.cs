using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[Authorize]
[Route("Bookings")]
public class BookingsController(BookingService bookings, CourtService courts, CourtAvailabilityService availability) : Controller
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
    public async Task<IActionResult> Create(int courtId, string? date, string? startTime)
    {
        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(startTime, out var parsedStart))
            return Content("Selecciona una fecha válida.", "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        try
        {
            await bookings.CreateAsync(CurrentUserId(), courtId, parsedDate, parsedStart);
        }
        catch (BookingNotFoundException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(404);
        }
        catch (BookingValidationException ex)
        {
            return Content(ex.Message, "text/plain", System.Text.Encoding.UTF8).WithStatus(400);
        }
        catch (BookingConflictException)
        {
            ViewData["Fecha"] = parsedDate.ToString("yyyy-MM-dd");
            Response.StatusCode = 409;
            return View("Conflict");
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
