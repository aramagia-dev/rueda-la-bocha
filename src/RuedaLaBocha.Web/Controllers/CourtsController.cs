using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

[Authorize(Roles = AppRoles.Administrator)]
public class CourtsController(CourtService courts) : Controller
{
    public async Task<IActionResult> Manage() => View(await courts.ListAsync(includeInactive: true));

    public IActionResult Create() => View("Form", new CourtForm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourtForm form)
    {
        if (ModelState.TryGetValue(nameof(CourtForm.IsActive), out var state) && state.Errors.Count > 0)
            return BadRequest("Active must be true or false.");
        if (!ModelState.IsValid) return View("Form", form);
        await courts.CreateAsync(form);
        return RedirectToAction(nameof(Manage));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var court = await courts.FindAsync(id);
        if (court is null) return NotFound();
        return View("Form", new CourtForm { Name = court.Name, Description = court.Description, IsActive = court.IsActive });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CourtForm form)
    {
        if (await courts.FindAsync(id) is null) return NotFound();
        if (ModelState.TryGetValue(nameof(CourtForm.IsActive), out var state) && state.Errors.Count > 0)
            return BadRequest("Active must be true or false.");
        if (!ModelState.IsValid) return View("Form", form);
        if (!await courts.UpdateAsync(id, form)) return NotFound();
        return RedirectToAction(nameof(Manage));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        if (!await courts.DeactivateAsync(id)) return NotFound();
        return RedirectToAction(nameof(Manage));
    }
}
