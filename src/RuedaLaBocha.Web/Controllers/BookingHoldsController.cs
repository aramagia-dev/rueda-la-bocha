using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

// Pago TOTAL con Padi sandbox: la reserva NO se crea hasta cobrarse.
// Flujo: POST crea un hold de 15 min + enlace SingleUse en ARS con el importe
// fijado en el servidor (BookingPolicy.PriceArs) -> el slot queda bloqueado en
// disponibilidad -> POST Status por polling crea el Booking Active con precio
// histórico solo ante Succeeded con mismo enlace + importe + moneda.
// Vencido/Fallido/Cancelado libera el slot sin crear reserva.
// Sin webhooks, sin background jobs, sin reembolsos.
[Authorize]
[Route("Bookings/Holds")]
public class BookingHoldsController(
    ApplicationDbContext db,
    CourtService courts,
    CourtAvailabilityService availability,
    IPadiClient padi,
    ILogger<BookingHoldsController> logger) : Controller
{
    // Crea el hold y su enlace SingleUse. Nunca reintenta el POST: sin
    // idempotencia, reintentar crearía un segundo enlace. Ante timeout el hold
    // queda pendiente sin enlace y se reintenta con Link.
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int courtId, string? date, string? startTime, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(startTime, out var parsedStart))
            return Plain("Selecciona una fecha válida.", 400);
        var court = await courts.FindAsync(courtId);
        if (court is null) return Plain(BookingService.UnknownCourtMessage, 404);
        if (!court.IsActive) return Plain(BookingService.InactiveCourtMessage, 400);
        if (parsedDate < DateOnly.FromDateTime(DateTime.Today))
            return Plain(BookingService.PastSlotMessage, 400);
        if (await db.Bookings.AnyAsync(b => b.CourtId == courtId && b.Date == parsedDate &&
            b.StartTime == parsedStart && b.Status == BookingStatus.Active, ct))
            return Plain(BookingConflictException.SlotTakenMessage, 409);
        if (!(await availability.GetAvailableStartTimesAsync(courtId, parsedDate)).Contains(parsedStart))
        {
            var schedules = await db.CourtSchedules.AsNoTracking()
                .AnyAsync(s => s.CourtId == courtId && s.DayOfWeek == parsedDate.DayOfWeek, ct);
            if (!schedules) return Plain(BookingService.NoScheduleMessage, 400);
            return Plain(BookingService.BlockedSlotMessage, 400);
        }

        var now = DateTime.UtcNow;
        // H1: un hold vencido-aún-Pending sigue bloqueando el índice único
        // aunque disponibilidad ya muestra el slot libre. Se libera y se continúa.
        var stale = await db.BookingHolds
            .Where(h => h.CourtId == courtId && h.Date == parsedDate && h.StartTime == parsedStart &&
                h.Status == BookingHoldStatus.Pending && h.ExpiresAtUtc <= now)
            .ToListAsync(ct);
        foreach (var old in stale)
        {
            old.Status = BookingHoldStatus.Released;
            old.UpdatedAtUtc = now;
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);

        var hold = new BookingHold
        {
            CourtId = courtId,
            UserId = CurrentUserId(),
            Date = parsedDate,
            StartTime = parsedStart,
            EndTime = parsedStart.Add(TimeSpan.FromMinutes(BookingPolicy.DurationMinutes)),
            AmountArs = BookingPolicy.PriceArs,
            Currency = "ARS",
            Status = BookingHoldStatus.Pending,
            ExpiresAtUtc = now.AddMinutes(BookingHold.HoldMinutes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.BookingHolds.Add(hold);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Plain(BookingConflictException.SlotTakenMessage, 409);
        }

        try
        {
            var link = await padi.CreateLinkAsync(
                hold.AmountArs,
                $"Reserva #{hold.Id} — Rueda la Bocha",
                ct,
                BookingHold.HoldMinutes);
            hold.PadiLinkId = link.Id;
            hold.CheckoutUrl = link.CheckoutUrl;
            hold.QrCodeUrl = link.QrCodeUrl;
            hold.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Padi demo: no se pudo crear el enlace para el hold {HoldId}; queda pendiente", hold.Id);
            TempData["HoldError"] = "No se pudo crear el enlace de pago (demo). Probá más tarde con Reintentar.";
        }
        return RedirectToAction(nameof(Detail), new { id = hold.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var (hold, forbidden) = await GetForViewerAsync(id, ct);
        if (forbidden) return HoldForbidden(BookingService.ForbiddenMessage);
        if (hold is null) return NotFound();
        return View(hold);
    }

    // Reintento manual del enlace cuando el POST inicial falló (hold pendiente
    // sin enlace). Si ya tiene enlace redirige al detalle sin crear otro.
    [HttpPost("{id:int}/Link")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLink(int id, CancellationToken ct)
    {
        var (hold, forbidden) = await GetForViewerAsync(id, ct);
        if (forbidden) return HoldForbidden(BookingService.ForbiddenMessage);
        if (hold is null) return NotFound();
        var tracked = hold;
        if (tracked.PadiLinkId is not null || tracked.Status != BookingHoldStatus.Pending ||
            tracked.IsExpired(DateTime.UtcNow))
            return RedirectToAction(nameof(Detail), new { id });

        try
        {
            var link = await padi.CreateLinkAsync(
                tracked.AmountArs,
                $"Reserva #{tracked.Id} — Rueda la Bocha",
                ct,
                BookingHold.HoldMinutes);
            tracked.PadiLinkId = link.Id;
            tracked.CheckoutUrl = link.CheckoutUrl;
            tracked.QrCodeUrl = link.QrCodeUrl;
            tracked.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Padi demo: reintento de enlace fallido para el hold {HoldId}", id);
            TempData["HoldError"] = "No se pudo crear el enlace de pago (demo). Probá más tarde con Reintentar.";
        }
        return RedirectToAction(nameof(Detail), new { id });
    }

    // Una pasada de polling: consulta Padi ANTES de liberar por vencimiento —
    // el pago pudo acreditarse después de cerrar la página o de vencer la
    // espera. Solo Succeeded con mismo enlace + importe + moneda y hold aún
    // vigente crea el Booking Active con precio histórico y marca el hold
    // consumido. Vencido con pago válido NO se confirma automáticamente:
    // queda Released con el PadiPaymentId como evidencia para conciliación
    // manual (nunca reserva fantasma). Reintentable sin doble reserva: el
    // estado Consumed + el índice único la impiden. Es POST porque muta estado.
    // Sin webhooks, sin background jobs, sin reembolsos.
    [HttpPost("{id:int}/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(int id, CancellationToken ct)
    {
        var (hold, forbidden) = await GetForViewerAsync(id, ct);
        if (forbidden) return HoldForbidden(BookingService.ForbiddenMessage);
        if (hold is null) return NotFound();
        var tracked = hold;

        // Ya consumido: idempotente, nunca crea otra reserva.
        if (tracked.Status == BookingHoldStatus.Consumed)
            return HoldJson(tracked);

        var expired = tracked.Status == BookingHoldStatus.Pending && tracked.IsExpired(DateTime.UtcNow);

        // El pago pudo llegar tarde (página cerrada, polling detenido): se
        // consulta Padi aunque haya vencido o esté liberado sin reserva.
        PadiReceivedPayment? match = null;
        if (tracked.PadiLinkId is not null && tracked.BookingId is null)
        {
            try
            {
                var received = await padi.ListReceivedPaymentsAsync(100, ct);
                foreach (var candidate in received.Where(r =>
                    string.Equals(r.PaymentLinkId, tracked.PadiLinkId, StringComparison.OrdinalIgnoreCase)))
                {
                    var confirmed = await padi.GetPaymentAsync(candidate.Id, ct);
                    if (confirmed is not null &&
                        string.Equals(confirmed.PaymentLinkId, tracked.PadiLinkId, StringComparison.OrdinalIgnoreCase))
                    {
                        match = confirmed;
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Padi demo: polling fallido para el hold {HoldId}; sigue pendiente", id);
                if (!expired && tracked.Status == BookingHoldStatus.Pending)
                    return HoldJson(tracked);
            }
        }

        if (match is not null)
        {
            if (string.Equals(match.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                match.Amount == tracked.AmountArs &&
                string.Equals(match.Currency, "ARS", StringComparison.OrdinalIgnoreCase))
            {
                if (tracked.Status == BookingHoldStatus.Pending && !expired)
                {
                    var taken = await db.Bookings.AnyAsync(b => b.CourtId == tracked.CourtId &&
                        b.Date == tracked.Date && b.StartTime == tracked.StartTime &&
                        b.Status == BookingStatus.Active, ct);
                    if (!taken)
                    {
                        var booking = new Booking
                        {
                            CourtId = tracked.CourtId,
                            UserId = tracked.UserId,
                            Date = tracked.Date,
                            StartTime = tracked.StartTime,
                            EndTime = tracked.EndTime,
                            PriceArs = tracked.AmountArs,
                            Status = BookingStatus.Active,
                            CreatedAtUtc = DateTime.UtcNow,
                        };
                        db.Bookings.Add(booking);
                        try
                        {
                            await db.SaveChangesAsync(ct);
                            tracked.Status = BookingHoldStatus.Consumed;
                            tracked.BookingId = booking.Id;
                            tracked.PadiPaymentId = match.Id;
                        }
                        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                        {
                            db.Entry(booking).State = EntityState.Detached;
                            // H3: un intento previo pudo guardar el Booking pero no la
                            // transición del hold; reclamar la propia en vez de liberar.
                            var own = await db.Bookings.FirstOrDefaultAsync(b => b.CourtId == tracked.CourtId &&
                                b.Date == tracked.Date && b.StartTime == tracked.StartTime &&
                                b.Status == BookingStatus.Active && b.UserId == tracked.UserId, ct);
                            if (own is not null)
                            {
                                tracked.Status = BookingHoldStatus.Consumed;
                                tracked.BookingId = own.Id;
                            }
                            else
                            {
                                logger.LogWarning("Padi demo: turno ocupado al confirmar el hold {HoldId}; se libera", id);
                                tracked.Status = BookingHoldStatus.Released;
                            }
                            tracked.PadiPaymentId = match.Id;
                        }
                    }
                    else
                    {
                        // H3: igual que arriba, reclamar la propia huérfana si existe.
                        var own = await db.Bookings.FirstOrDefaultAsync(b => b.CourtId == tracked.CourtId &&
                            b.Date == tracked.Date && b.StartTime == tracked.StartTime &&
                            b.Status == BookingStatus.Active && b.UserId == tracked.UserId, ct);
                        if (own is not null)
                        {
                            tracked.Status = BookingHoldStatus.Consumed;
                            tracked.BookingId = own.Id;
                        }
                        else
                        {
                            logger.LogWarning("Padi demo: turno ocupado al confirmar el hold {HoldId}; se libera", id);
                            tracked.Status = BookingHoldStatus.Released;
                        }
                        tracked.PadiPaymentId = match.Id;
                    }
                }
                else
                {
                    // Pago válido pero fuera de vigencia (o hold ya liberado):
                    // sin confirmación automática; queda evidencia para conciliar.
                    logger.LogWarning(
                        "Padi demo: pago {PaymentId} posterior al vencimiento para el hold {HoldId}; no se confirma, queda evidencia",
                        match.Id, id);
                    if (tracked.Status == BookingHoldStatus.Pending)
                        tracked.Status = BookingHoldStatus.Released;
                    tracked.PadiPaymentId ??= match.Id;
                }
            }
            else if (string.Equals(match.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                if (tracked.Status == BookingHoldStatus.Pending)
                    tracked.Status = BookingHoldStatus.Released;
                tracked.PadiPaymentId ??= match.Id;
            }
            else if (string.Equals(match.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                if (tracked.Status == BookingHoldStatus.Pending)
                    tracked.Status = BookingHoldStatus.Released;
                tracked.PadiPaymentId ??= match.Id;
            }
            else if (match.Amount != tracked.AmountArs ||
                !string.Equals(match.Currency, "ARS", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Padi demo: pago {PaymentId} con importe/moneda distintos para el hold {HoldId}; no se aprueba",
                    match.Id, id);
                if (expired && tracked.Status == BookingHoldStatus.Pending)
                {
                    tracked.Status = BookingHoldStatus.Released;
                    tracked.PadiPaymentId ??= match.Id;
                }
            }
            tracked.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        else if (expired && tracked.Status == BookingHoldStatus.Pending)
        {
            tracked.Status = BookingHoldStatus.Released;
            tracked.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return HoldJson(tracked);
        }

        return HoldJson(tracked);
    }

    private static JsonResult HoldJson(BookingHold hold) => new(new
    {
        approved = hold.Status == BookingHoldStatus.Consumed,
        status = hold.Status.ToString(),
        checkoutUrl = hold.CheckoutUrl,
        qrCodeUrl = hold.QrCodeUrl,
        bookingId = hold.BookingId,
    });

    private async Task<(BookingHold? Hold, bool Forbidden)> GetForViewerAsync(int id, CancellationToken ct)
    {
        var hold = await db.BookingHolds.Include(h => h.Court)
            .SingleOrDefaultAsync(h => h.Id == id, ct);
        if (hold is null) return (null, false);
        if (!User.IsInRole(AppRoles.Administrator) && hold.UserId != CurrentUserId())
            return (null, true);
        return (hold, false);
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    private static ContentResult Plain(string message, int statusCode) => new()
    {
        Content = message,
        ContentType = "text/plain; charset=utf-8",
        StatusCode = statusCode,
    };

    private static ContentResult HoldForbidden(string message) => Plain(message, StatusCodes.Status403Forbidden);

    internal static bool IsUniqueViolation(DbUpdateException ex)
    {
        var text = ex.InnerException?.Message ?? ex.Message;
        // Igual que BookingService: SQLite "UNIQUE constraint failed", SQL Server 2601/2627.
        return text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }
}
