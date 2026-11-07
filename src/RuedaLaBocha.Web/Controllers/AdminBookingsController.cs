using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
[Route("Admin/Bookings")]
public class AdminBookingsController(BookingService bookings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? date, int? courtId, string? status, string? userId)
    {
        BookingStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: true, out BookingStatus value))
                return BadRequest("Selecciona una fecha válida.");
            parsedStatus = value;
        }
        if (!ModelState.IsValid) return BadRequest("Selecciona una fecha válida.");
        return View(await bookings.AdminListAsync(new BookingFilter(date, courtId, parsedStatus, userId)));
    }

    [HttpPost("{id:int}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var caller = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        try
        {
            var (_, wasAlreadyCancelled) = await bookings.CancelAsync(id, caller, isAdmin: true);
            if (wasAlreadyCancelled)
                return Content(BookingService.AlreadyCancelledMessage, "text/plain", System.Text.Encoding.UTF8).WithStatus(200);
        }
        catch (BookingNotFoundException)
        {
            return NotFound();
        }
        TempData["BookingMessage"] = "Tu reserva fue cancelada. El horario quedó libre nuevamente.";
        return RedirectToAction(nameof(Index));
    }
}

file static class AdminContentResultExtensions
{
    public static ContentResult WithStatus(this ContentResult result, int statusCode)
    {
        result.StatusCode = statusCode;
        return result;
    }
}
