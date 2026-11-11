using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class ReservaCalendarioTests
{
    [Fact]
    public async Task Calendario_inicial_no_muestra_canchas_y_bloquea_pasado()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync("/Reservas");
        Assert.Contains("Calendario", html);
        Assert.Contains("vc-calendario", html);
        Assert.Contains("vanilla-calendar-pro.js", html);
        Assert.Contains("vanilla-calendar-pro.css", html);
        Assert.Contains("locale: 'es'", html);
        Assert.Contains("selectionDatesMode: 'single'", html);
        Assert.Contains("selectedTheme: 'light'", html);
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        Assert.Contains("displayDateMin", html);
        Assert.Contains(today, html);
        Assert.Contains("type=\"date\"", html);
        Assert.Contains("name=\"fecha\"", html);
        Assert.Contains($"min=\"{today}\"", html);
        Assert.DoesNotContain("Reservar turno", html);
        var past = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)).ToString("yyyy-MM-dd");
        Assert.DoesNotContain($"fecha={past}\"", html);
        Assert.DoesNotContain("mes-anterior", html);
    }

    [Fact]
    public async Task Fecha_elegida_muestra_solo_activas_con_huecos_y_enlaza_reserva()
    {
        using var app = new TestApp();
        int activeId, emptyId, inactiveId;
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Activa libre", Description = "t", IsActive = true });
            await courts.CreateAsync(new CourtForm { Name = "Activa sin huecos", Description = "t", IsActive = true });
            await courts.CreateAsync(new CourtForm { Name = "Inactiva", Description = "t", IsActive = false });
            var all = await courts.ListAsync(includeInactive: true);
            activeId = all.Single(c => c.Name == "Activa libre").Id;
            emptyId = all.Single(c => c.Name == "Activa sin huecos").Id;
            inactiveId = all.Single(c => c.Name == "Inactiva").Id;
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            monday = Next(DayOfWeek.Monday);
            await schedules.CreateScheduleAsync(activeId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
            await schedules.CreateScheduleAsync(inactiveId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        }
        using var guest = app.Client();
        var html = await guest.GetStringAsync($"/Reservas?fecha={monday:yyyy-MM-dd}");
        Assert.Contains("Activa libre", html);
        Assert.Contains($"courtId={activeId}", html);
        Assert.Contains("09:00", html);
        Assert.Contains("/Bookings/Create?", html);
        Assert.Contains(monday.ToString("yyyy-MM-dd"), html);
        Assert.DoesNotContain("Activa sin huecos", html);
        Assert.DoesNotContain("Inactiva", html);

        var tuesday = monday.AddDays(1);
        var empty = await guest.GetStringAsync($"/Reservas?fecha={tuesday:yyyy-MM-dd}");
        Assert.Contains("cambiar de fecha", empty);
    }

    [Fact]
    public async Task Crear_exige_auth_y_confirmacion_explicita_con_precio()
    {
        using var app = new TestApp();
        int courtId;
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Confirm court", Description = "t", IsActive = true });
            courtId = (await courts.ListAsync(includeInactive: true)).Single().Id;
            monday = Next(DayOfWeek.Monday);
            await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
                .CreateScheduleAsync(courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0));
        }
        using var guest = app.Client();
        var challenge = await guest.GetAsync($"/Bookings/Create?courtId={courtId}&date={monday:yyyy-MM-dd}&startTime=09:00");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Contains("Login", challenge.Headers.Location!.ToString());
        Assert.Contains("ReturnUrl", challenge.Headers.Location!.ToString());

        using var owner = await Login(app, "confirm@example.test");
        var confirm = await owner.GetStringAsync($"/Bookings/Create?courtId={courtId}&date={monday:yyyy-MM-dd}&startTime=09:00");
        Assert.Contains("Confirmar reserva", confirm);
        Assert.Contains("40.000", confirm);
        Assert.Contains("__RequestVerificationToken", confirm);

        var created = await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = monday.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" }, "/Bookings");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var conflict = await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = monday.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" }, "/Bookings");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("ya fue reservado", conflictHtml);
        Assert.Contains($"/Reservas?fecha={monday:yyyy-MM-dd}", conflictHtml);
        Assert.Contains("Volver a consultar", conflictHtml);
    }

    [Fact]
    public async Task ReturnUrl_preserva_seleccion_y_login_vuelve_a_confirmar_sin_crear_en_get()
    {
        using var app = new TestApp();
        int courtId;
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Return court", Description = "t", IsActive = true });
            courtId = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == "Return court").Id;
            monday = Next(DayOfWeek.Monday);
            await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
                .CreateScheduleAsync(courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0));
        }
        using var guest = app.Client();
        var challenge = await guest.GetAsync($"/Bookings/Create?courtId={courtId}&date={monday:yyyy-MM-dd}&startTime=09:00");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var location = challenge.Headers.Location!.ToString();
        Assert.Contains("ReturnUrl=", location);
        Assert.Contains("courtId%3D" + courtId, location);
        Assert.Contains(monday.ToString("yyyy-MM-dd"), System.Net.WebUtility.UrlDecode(location));
        // Sin login no se crea nada vía GET (302 a Login, sin escritura).
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        using var owner = await Login(app, "return@example.test");
        // POST con usuario pero sin token se rechaza sin escribir.
        var before = app.Snapshot();
        var tokenless = await owner.PostAsync("/Bookings", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["courtId"] = courtId.ToString(), ["date"] = monday.ToString("yyyy-MM-dd"), ["startTime"] = "09:00"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, tokenless.StatusCode);
        Assert.Equal(before, app.Snapshot());
        // Login con ReturnUrl local vuelve a confirmar.
        var confirm = await owner.GetStringAsync($"/Bookings/Create?courtId={courtId}&date={monday:yyyy-MM-dd}&startTime=09:00");
        Assert.Contains("Confirmar reserva", confirm);
        Assert.Contains(monday.ToString("dd/MM/yyyy"), confirm);
    }

    [Fact]
    public async Task Fecha_pasada_e_invalida_se_rechazan_sin_escribir()
    {
        using var app = new TestApp();
        int courtId;
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Past court", Description = "t", IsActive = true });
            courtId = (await courts.ListAsync(includeInactive: true)).Single().Id;
            monday = Next(DayOfWeek.Monday);
            await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
                .CreateScheduleAsync(courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0));
        }
        using var guest = app.Client();
        var past = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)).ToString("yyyy-MM-dd");
        var pastHtml = await guest.GetStringAsync($"/Reservas?fecha={past}");
        Assert.Contains("futura", pastHtml);
        Assert.DoesNotContain("Reservar turno", pastHtml);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/Reservas?fecha=no-fecha")).StatusCode);
        using var owner = await Login(app, "past@example.test");
        var before = app.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/Bookings/Create?courtId={courtId}&date={past}&startTime=09:00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/Bookings/Create?courtId={courtId}&date=no-fecha&startTime=09:00")).StatusCode);
        var rejected = await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = past, ["startTime"] = "09:00" }, "/Bookings");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before, app.Snapshot());
    }

    [Fact]
    public async Task Calendario_vendor_acepta_bisiesto_y_rechaza_pasada()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        // Bisiesto futuro se parsea sin romper (sin canchas => mensaje de cambiar fecha).
        var leap = await guest.GetStringAsync("/Reservas?fecha=2028-02-29");
        Assert.Contains("vc-calendario", leap);
        Assert.Contains("cambiar de fecha", leap);
        // Bisiesto pasado se rechaza como fecha pasada sin escribir.
        var pastLeap = await guest.GetStringAsync("/Reservas?fecha=2024-02-29");
        Assert.Contains("futura", pastLeap);
        Assert.DoesNotContain("Reservar turno", pastLeap);
        // Parámetro legacy ?mes se ignora sin romper (el vendor navega meses en cliente).
        foreach (var legacy in new[] { "/Reservas?mes=2024-02", "/Reservas?mes=2026-12", "/Reservas?mes=no-mes" })
        {
            var response = await guest.GetAsync(legacy);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("vc-calendario", await response.Content.ReadAsStringAsync());
        }
        // Hoy seleccionada se refleja en selectedDates del vendor.
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var selected = await guest.GetStringAsync($"/Reservas?fecha={today}");
        Assert.Contains(today, selected);
        Assert.Contains("selectedDates", selected);
    }

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
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
