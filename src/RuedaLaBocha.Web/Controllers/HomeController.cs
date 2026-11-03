using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

public class HomeController(CourtService courts) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View((await courts.ListAsync()).Take(3).ToArray());
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
