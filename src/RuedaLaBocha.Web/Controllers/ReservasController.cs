using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[AllowAnonymous]
public class ReservasController(
    CourtService courts,
    ICourtAvailabilityService availability,
    ILogger<ReservasController> logger) : Controller
{
    [HttpGet("/Reservas")]
    public async Task<IActionResult> Index(string? fecha)
    {
        var (model, status) = await BuildAsync(fecha);
        Response.StatusCode = status;
        return View(model);
    }

    // Fragmento para la consulta sin recarga (fetch): mismo modelo y estados
    // que la página completa, sin layout ni vendor. Reutiliza los servicios.
    [HttpGet("/Reservas/Resultados")]
    public async Task<IActionResult> Resultados(string? fecha)
    {
        var (model, status) = await BuildAsync(fecha);
        Response.StatusCode = status;
        return PartialView("_Resultados", model);
    }

    private async Task<(ReservaIndexViewModel Model, int Status)> BuildAsync(string? fecha)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly? selected = null;
        if (fecha is not null)
        {
            if (!DateOnly.TryParse(fecha, out var parsed))
            {
                return (new ReservaIndexViewModel(today, null, [],
                    "Seleccioná una fecha válida para ver las canchas con turnos libres."), 400);
            }
            selected = parsed;
        }

        var results = new List<ReservaCourtSlots>();
        string? message = null;
        if (selected.HasValue)
        {
            if (selected.Value < today)
                message = "No se puede reservar un horario pasado. Elegí una fecha futura.";
            else
            {
                try
                {
                    foreach (var court in await courts.ListAsync())
                    {
                        var slots = await availability.GetAvailableStartTimesAsync(court.Id, selected.Value);
                        if (slots.Count > 0) results.Add(new ReservaCourtSlots(court, slots));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Sin datos sensibles del usuario: solo la fecha consultada.
                    logger.LogError(ex, "No se pudo consultar la disponibilidad para {Fecha}.", selected.Value);
                    return (new ReservaIndexViewModel(today, selected, [],
                        "No pudimos consultar los turnos de esta fecha. Probá reintentar."), 503);
                }
                if (results.Count == 0)
                    message = "No hay canchas con turnos libres para esta fecha. Probá cambiar de fecha.";
            }
        }
        return (new ReservaIndexViewModel(today, selected, results, message), 200);
    }
}
