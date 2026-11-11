using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

// E1 agenda por fecha (Penpot CORREGIDO): hero íntegro + calendario existente +
// canchas en acordeón con hora explícita y resumen. Solo render server; sin negocio nuevo.
[Collection("Court integration")]
public sealed class E1AgendaTests
{
    [Fact]
    public async Task E1_hero_y_nav_con_aviso_de_pagos_sin_fraude()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = WebUtility.HtmlDecode(await guest.GetStringAsync("/Reservas"));
        Assert.Contains("Tu próximo partido", html);
        Assert.Contains("empieza acá", html);
        Assert.Contains("RESERVAS ONLINE", html);
        Assert.Contains("Buscar disponibilidad", html);
        Assert.DoesNotContain("Cómo funciona", html);
        Assert.DoesNotContain("como-funciona", html);
        Assert.DoesNotContain(">Reservar<", html);
        Assert.DoesNotContain("href=\"/Courts\"", html);
        Assert.Contains("SEÑA ONLINE", html);
        Assert.Contains("no hay pagos implementados", html);
        Assert.DoesNotContain("/Payments", html);
        Assert.Contains(">Inicio<", html);
        Assert.Contains("action=\"/Reservas\"", html);
        Assert.Contains("/Reservas/Resultados?fecha=", html);
        Assert.DoesNotContain("Mis reservas", html);
        using var customer = await Login(app, "e1@example.test");
        var authed = WebUtility.HtmlDecode(await customer.GetStringAsync("/Reservas"));
        Assert.Contains("Mis reservas", authed);
        Assert.Contains("/Bookings/Mine", authed);
    }

    [Fact]
    public async Task E1_acordeon_una_abierta_con_hora_explicita_y_resumen()
    {
        using var app = new TestApp();
        int firstId;
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Cancha Uno", Description = "t", IsActive = true });
            await courts.CreateAsync(new CourtForm { Name = "Cancha Dos", Description = "t", IsActive = true });
            var unoId = (await courts.ListAsync()).Single(c => c.Name == "Cancha Uno").Id;
            var dosId = (await courts.ListAsync()).Single(c => c.Name == "Cancha Dos").Id;
            firstId = dosId; // ListAsync ordena por nombre: "Cancha Dos" se muestra primera.
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            monday = Next(DayOfWeek.Monday);
            await schedules.CreateScheduleAsync(unoId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0));
            await schedules.CreateScheduleAsync(dosId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        }
        using var guest = app.Client();
        var html = WebUtility.HtmlDecode(await guest.GetStringAsync($"/Reservas?fecha={monday:yyyy-MM-dd}"));
        Assert.True(Regex.Matches(html, "aria-expanded=\"true\"").Count == 1);
        Assert.True(Regex.Matches(html, "class=\"e1-toggle\" id=\"e1-btn-[^\"]+\" aria-expanded=\"false\"").Count == 1);
        Assert.True(Regex.Matches(html, "aria-controls=\"e1-panel-").Count == 2);
        Assert.Contains("e1-panel-inner", html);
        Assert.True(Regex.Matches(html, "e1-panel is-closed").Count == 1);
        Assert.Contains("type=\"radio\"", html);
        Assert.Contains("name=\"turno\"", html);
        Assert.Contains("Cancha Uno", html);
        Assert.Contains("Cancha Dos", html);
        Assert.Contains("09:00", html);
        Assert.Contains("/Bookings/Create?", html);
        Assert.Contains($"courtId={firstId}", html);
        Assert.Contains("se conserva al login", html);
        Assert.Contains("Reservar turno", html);
        Assert.Contains("e1-reservar-hora", html);
    }

    [Fact]
    public async Task E1_vacia_error_e_invalida_ofrecen_reintento()
    {
        using var app = new TestApp();
        int courtId;
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Cancha E1", Description = "t", IsActive = true });
            courtId = (await courts.ListAsync()).Single().Id;
            monday = Next(DayOfWeek.Monday);
            await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
                .CreateScheduleAsync(courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        }
        using var guest = app.Client();
        var empty = WebUtility.HtmlDecode(await guest.GetStringAsync($"/Reservas?fecha={monday.AddDays(1):yyyy-MM-dd}"));
        Assert.Contains("cambiar de fecha", empty);
        Assert.Contains("Elegir otra fecha", empty);

        var past = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)).ToString("yyyy-MM-dd");
        var pastHtml = WebUtility.HtmlDecode(await guest.GetStringAsync($"/Reservas?fecha={past}"));
        Assert.Contains("futura", pastHtml);
        Assert.Contains("Elegir otra fecha", pastHtml);
        Assert.DoesNotContain("Reservar turno", pastHtml);

        var invalid = await guest.GetAsync("/Reservas?fecha=no-fecha");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidHtml = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
        Assert.Contains("fecha válida", invalidHtml);
        Assert.Contains("Elegir otra fecha", invalidHtml);
    }

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    [Fact]
    public async Task E1_error_consulta_devuelve_503_con_reintento_misma_fecha()
    {
        using var app = new UnavailableApp();
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Cancha error", Description = "t", IsActive = true });
            monday = Next(DayOfWeek.Monday);
        }
        using var guest = app.Client();
        var before = app.Snapshot();
        var response = await guest.GetAsync($"/Reservas?fecha={monday:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("reintentar", html);
        Assert.Contains("Reintentar", html);
        Assert.Contains($"/Reservas?fecha={monday:yyyy-MM-dd}", html);
        Assert.Contains("vc-calendario", html);
        Assert.DoesNotContain("Reservar turno", html);
        Assert.DoesNotContain("court-card", html);
        Assert.DoesNotContain("falla simulada", html);
        Assert.Equal(before, app.Snapshot());
    }

    private sealed class UnavailableApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICourtAvailabilityService>();
                services.AddSingleton<ICourtAvailabilityService, ThrowingAvailability>();
            });
        }

        private sealed class ThrowingAvailability : ICourtAvailabilityService
        {
            public Task<List<TimeOnly>> GetAvailableStartTimesAsync(int courtId, DateOnly date) =>
                Task.FromException<List<TimeOnly>>(new InvalidOperationException("falla simulada"));
        }
    }

    private static async Task<HttpClient> Login(TestApp app, string email)
    {
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
            var user = new Microsoft.AspNetCore.Identity.IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        }
        var client = app.Client();
        var response = await CourtFlowTests.Post(client, "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
}
