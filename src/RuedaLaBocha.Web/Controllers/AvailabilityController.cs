using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public class AvailabilityController(CourtService courts, CourtScheduleService hours, CourtAvailabilityService availability) : Controller
{
    [AllowAnonymous]
    public async Task<IActionResult> Index(int courtId, DateOnly? date)
    {
        var court = await courts.FindAsync(courtId);
        if (court is null || !court.IsActive) return NotFound();
        if (!ModelState.IsValid) return BadRequest("Selecciona una fecha válida.");
        var selected = date ?? DateOnly.FromDateTime(DateTime.Today);
        return View(new CourtAvailabilityViewModel(court, selected, await availability.GetAvailableStartTimesAsync(courtId, selected)));
    }

    public async Task<IActionResult> Manage(int courtId)
    {
        var court = await courts.FindAsync(courtId);
        if (court is null) return NotFound();
        return View(new CourtHoursViewModel(court, await hours.ListSchedulesAsync(courtId), await hours.ListClosuresAsync(courtId)));
    }

    public async Task<IActionResult> Schedule(int courtId, int? id)
    {
        ViewData["RuleId"] = id;
        var court = await courts.FindAsync(courtId);
        if (court is null) return NotFound();
        ViewData["Court"] = court;
        if (!id.HasValue) return View(new ScheduleForm());
        var schedule = (await hours.ListSchedulesAsync(courtId)).SingleOrDefault(item => item.Id == id);
        if (schedule is null) return NotFound();
        return View(new ScheduleForm { DayOfWeek = schedule.DayOfWeek, StartTime = schedule.StartTime, EndTime = schedule.EndTime });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Schedule(int courtId, int? id, ScheduleForm form)
    {
        ViewData["RuleId"] = id;
        var court = await courts.FindAsync(courtId);
        if (court is null) return NotFound();
        if (id.HasValue && !(await hours.ListSchedulesAsync(courtId)).Any(item => item.Id == id)) return NotFound();
        ViewData["Court"] = court;
        if (!ModelState.IsValid) return View(form);
        try
        {
            if (id.HasValue)
            {
                if (!await hours.UpdateScheduleAsync(id.Value, form.DayOfWeek!.Value, form.StartTime!.Value, form.EndTime!.Value)) return NotFound();
            }
            else await hours.CreateScheduleAsync(courtId, form.DayOfWeek!.Value, form.StartTime!.Value, form.EndTime!.Value);
        }
        catch (InvalidOperationException)
        {
            ModelState.AddModelError("", "El horario se superpone con otro intervalo de este día.");
            return View(form);
        }
        return RedirectToAction(nameof(Manage), new { courtId });
    }

    public async Task<IActionResult> Closure(int courtId, int? id)
    {
        ViewData["RuleId"] = id;
        var court = await courts.FindAsync(courtId);
        if (court is null) return NotFound();
        ViewData["Court"] = court;
        if (!id.HasValue) return View(new ClosureForm());
        var closure = (await hours.ListClosuresAsync(courtId)).SingleOrDefault(item => item.Id == id);
        if (closure is null) return NotFound();
        return View(new ClosureForm { Date = closure.Date, StartTime = closure.StartTime, EndTime = closure.EndTime });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Closure(int courtId, int? id, ClosureForm form)
    {
        ViewData["RuleId"] = id;
        var court = await courts.FindAsync(courtId);
        if (court is null) return NotFound();
        if (id.HasValue && !(await hours.ListClosuresAsync(courtId)).Any(item => item.Id == id)) return NotFound();
        ViewData["Court"] = court;
        if (!ModelState.IsValid) return View(form);
        try
        {
            if (id.HasValue)
            {
                if (!await hours.UpdateClosureAsync(id.Value, form.Date!.Value, form.StartTime!.Value, form.EndTime!.Value)) return NotFound();
            }
            else await hours.CreateClosureAsync(courtId, form.Date!.Value, form.StartTime!.Value, form.EndTime!.Value);
        }
        catch (InvalidOperationException)
        {
            ModelState.AddModelError("", "El bloqueo se superpone con otro de esta fecha.");
            return View(form);
        }
        return RedirectToAction(nameof(Manage), new { courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSchedule(int courtId, int id)
    {
        if (!(await hours.ListSchedulesAsync(courtId)).Any(item => item.Id == id)) return NotFound();
        if (!await hours.DeleteScheduleAsync(id)) return NotFound();
        return RedirectToAction(nameof(Manage), new { courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteClosure(int courtId, int id)
    {
        if (!(await hours.ListClosuresAsync(courtId)).Any(item => item.Id == id)) return NotFound();
        if (!await hours.DeleteClosureAsync(id)) return NotFound();
        return RedirectToAction(nameof(Manage), new { courtId });
    }
}
