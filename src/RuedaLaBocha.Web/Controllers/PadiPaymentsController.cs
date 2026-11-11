using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

// Pago demo de la seña con Padi sandbox (https://padi-api.mintlify.site/).
// El importe se fija en el servidor desde la reserva; el cliente no envía
// montos. Padi no tiene webhooks: la confirmación es por polling del botón
// "Comprobar estado". Solo Succeeded con mismo enlace + importe + moneda
// marca aprobado; Pending/Processing queda pendiente, nunca aprobado.
// Sin auto-confirmar ni cancelar reservas, sin holds, sin background jobs.
[Authorize]
[Route("Bookings/{bookingId:int}/Padi")]
public class PadiPaymentsController(
    ApplicationDbContext db,
    BookingService bookings,
    IPadiClient padi,
    ILogger<PadiPaymentsController> logger) : Controller
{
    // Crea (o reutiliza) el enlace SingleUse en ARS para una reserva propia
    // activa. Nunca reintenta el POST: sin idempotencia, reintentar crearía
    // un segundo enlace. Ante timeout el resultado es incierto y la fila
    // queda pendiente para conciliar con "Comprobar estado".
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int bookingId, CancellationToken ct)
    {
        Booking? booking;
        try
        {
            booking = await bookings.GetForViewerAsync(bookingId, CurrentUserId(), User.IsInRole(AppRoles.Administrator));
        }
        catch (BookingForbiddenException ex)
        {
            return PadiForbidden(ex.Message);
        }
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Active)
        {
            TempData["PadiError"] = "Solo se puede pagar una reserva activa.";
            return RedirectToAction("Detail", "Bookings", new { id = bookingId });
        }

        var payment = await db.PadiPayments.FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment?.PadiLinkId is not null)
            return RedirectToAction("Detail", "Bookings", new { id = bookingId });

        payment ??= new PadiPayment
        {
            BookingId = bookingId,
            AmountArs = booking.PriceArs,
            Currency = "ARS",
            Status = PadiPaymentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        if (payment.Id == 0)
        {
            db.PadiPayments.Add(payment);
            await db.SaveChangesAsync(ct);
        }

        PadiLink link;
        try
        {
            link = await padi.CreateLinkAsync(
                payment.AmountArs,
                $"Reserva #{bookingId} — Rueda la Bocha",
                $"Seña reserva #{bookingId}",
                ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Padi demo: no se pudo crear el enlace para la reserva {BookingId}; queda pendiente", bookingId);
            TempData["PadiError"] = "No se pudo crear el enlace de pago (demo). Probá más tarde con Comprobar estado.";
            return RedirectToAction("Detail", "Bookings", new { id = bookingId });
        }

        payment.PadiLinkId = link.Id;
        payment.CheckoutUrl = link.CheckoutUrl;
        payment.QrCodeUrl = link.QrCodeUrl;
        payment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return RedirectToAction("Detail", "Bookings", new { id = bookingId });
    }

    // Una pasada de polling: busca pagos del enlace y confirma por id (fuente
    // de verdad). Solo actualiza a aprobado ante Succeeded con mismo enlace +
    // importe + moneda; cualquier otro caso queda pendiente o terminal.
    [HttpGet("Status")]
    public async Task<IActionResult> Status(int bookingId, CancellationToken ct)
    {
        Booking? booking;
        try
        {
            booking = await bookings.GetForViewerAsync(bookingId, CurrentUserId(), User.IsInRole(AppRoles.Administrator));
        }
        catch (BookingForbiddenException ex)
        {
            return PadiForbidden(ex.Message);
        }
        if (booking is null) return NotFound();

        var payment = await db.PadiPayments.AsNoTracking().FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment?.PadiLinkId is null) return NotFound();

        PadiReceivedPayment? match = null;
        try
        {
            var received = await padi.ListReceivedPaymentsAsync(100, ct);
            foreach (var candidate in received.Where(r =>
                string.Equals(r.PaymentLinkId, payment.PadiLinkId, StringComparison.OrdinalIgnoreCase)))
            {
                var confirmed = await padi.GetPaymentAsync(candidate.Id, ct);
                if (confirmed is not null &&
                    string.Equals(confirmed.PaymentLinkId, payment.PadiLinkId, StringComparison.OrdinalIgnoreCase))
                {
                    match = confirmed;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Padi demo: polling fallido para la reserva {BookingId}; sigue pendiente", bookingId);
            return Json(new { approved = false, status = payment.Status.ToString(), checkoutUrl = payment.CheckoutUrl, qrCodeUrl = payment.QrCodeUrl });
        }

        if (match is not null)
        {
            var tracked = await db.PadiPayments.FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
            if (tracked is not null)
            {
                if (string.Equals(match.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                    match.Amount == booking.PriceArs &&
                    string.Equals(match.Currency, "ARS", StringComparison.OrdinalIgnoreCase))
                {
                    tracked.Status = PadiPaymentStatus.Succeeded;
                    tracked.PadiPaymentId = match.Id;
                }
                else if (string.Equals(match.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    tracked.Status = PadiPaymentStatus.Failed;
                    tracked.PadiPaymentId = match.Id;
                }
                else if (string.Equals(match.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    tracked.Status = PadiPaymentStatus.Cancelled;
                    tracked.PadiPaymentId = match.Id;
                }
                else if (match.Amount != booking.PriceArs ||
                    !string.Equals(match.Currency, "ARS", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Padi demo: pago {PaymentId} con importe/moneda distintos para la reserva {BookingId}; no se aprueba",
                        match.Id, bookingId);
                }
                tracked.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                payment = tracked;
            }
        }

        return Json(new
        {
            approved = payment.Status == PadiPaymentStatus.Succeeded,
            status = payment.Status.ToString(),
            checkoutUrl = payment.CheckoutUrl,
            qrCodeUrl = payment.QrCodeUrl,
        });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    private ContentResult PadiForbidden(string message) => new()
    {
        Content = message,
        ContentType = "text/plain; charset=utf-8",
        StatusCode = StatusCodes.Status403Forbidden,
    };
}
