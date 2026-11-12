using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

// Subtarea 336-339: consulta de fecha sin recarga (partial + fetch con
// AbortController), paleta verde del calendario, tabla semántica en Mis reservas.
[Collection("Court integration")]
public sealed class ReservasFetchTests
{
    [Fact]
    public async Task Resultados_partial_devuelve_fragmento_sin_layout_para_fetch()
    {
        using var app = new TestApp();
        int courtId;
        DateOnly monday;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Cancha Fetch", Description = "t", IsActive = true });
            courtId = (await courts.ListAsync()).Single().Id;
            monday = Next(DayOfWeek.Monday);
            await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
                .CreateScheduleAsync(courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        }
        using var guest = app.Client();
        var response = await guest.GetAsync($"/Reservas/Resultados?fecha={monday:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.ToString());
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Cancha Fetch", html);
        Assert.Contains("09:00", html);
        Assert.Contains("Reservar turno", html);
        Assert.Contains("e1-acordeon", html);
        Assert.Contains("e1-resumen-texto", html);
        Assert.Contains("se conserva al login", html);
    }

    [Fact]
    public async Task Resultados_partial_no_repite_layout_ni_vendor()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync($"/Reservas/Resultados?fecha={Next(DayOfWeek.Monday):yyyy-MM-dd}");
        Assert.DoesNotContain("<html", html);
        Assert.DoesNotContain("main-navigation", html);
        Assert.DoesNotContain("id=\"vc-calendario\"", html);
        Assert.DoesNotContain("vanilla-calendar-pro", html);
    }

    [Fact]
    public async Task Resultados_partial_invalida_pasada_y_error_con_reintento()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var invalid = await guest.GetAsync("/Reservas/Resultados?fecha=no-fecha");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("fecha válida", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));

        var past = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)).ToString("yyyy-MM-dd");
        var pastHtml = await guest.GetStringAsync($"/Reservas/Resultados?fecha={past}");
        Assert.Contains("futura", pastHtml);
        Assert.DoesNotContain("Reservar turno", pastHtml);

        using var broken = new UnavailableApp();
        DateOnly monday;
        using (var scope = broken.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Cancha rota", Description = "t", IsActive = true });
            monday = Next(DayOfWeek.Monday);
        }
        using var guest2 = broken.Client();
        var before = broken.Snapshot();
        var error = await guest2.GetAsync($"/Reservas/Resultados?fecha={monday:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        var errorHtml = WebUtility.HtmlDecode(await error.Content.ReadAsStringAsync());
        Assert.Contains("reintentar", errorHtml);
        Assert.Contains($"/Reservas?fecha={monday:yyyy-MM-dd}", errorHtml);
        Assert.DoesNotContain("Reservar turno", errorHtml);
        Assert.DoesNotContain("falla simulada", errorHtml);
        Assert.Equal(before, broken.Snapshot());
    }

    [Fact]
    public async Task Reservas_fetch_js_consulta_sin_recarga_con_abort_e_historial()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync("/Reservas");
        Assert.Contains("/Reservas/Resultados?fecha=", html);
        Assert.Contains("AbortController", html);
        Assert.Contains("pushState", html);
        Assert.Contains("popstate", html);
        Assert.Contains("e1-estado", html);
        Assert.Contains("aria-live", html);
        Assert.DoesNotContain("window.location.href = '/Reservas?fecha='", html);
    }

    [Fact]
    public async Task Reservas_fallback_sigue_en_html_y_js_solo_oculta_con_vendor_ok()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync("/Reservas");
        Assert.Contains("type=\"date\"", html);
        Assert.Contains("name=\"fecha\"", html);
        Assert.Contains("Ver canchas", html);
        Assert.Contains("window.VanillaCalendarPro", html);
        Assert.Contains("vc-fallback", html);
        Assert.Contains("fallback) fallback.hidden = true", html);
    }

    [Fact]
    public async Task Calendario_paleta_verde_sin_azul_y_sin_global()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var css = await guest.GetStringAsync("/css/site.css");
        Assert.Contains("#vc-calendario", css);
        Assert.Contains("--vc-date-selected-bg", css);
        Assert.Contains("167751", css);
        Assert.Contains("--vc-focus-outline-color", css);
        Assert.Contains(".vc-fallback[hidden]", css);
        Assert.DoesNotContain("[data-vc-theme=light]", css);
    }

    [Fact]
    public async Task Mine_tabla_semantica_con_estado_es_y_acciones_sin_fuga()
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Cancha Tabla");
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "tabla@example.test");
        await SeedBooking(app, "tabla@example.test", courtId, date);

        var mine = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.Contains("<table", mine);
        Assert.Contains("<th scope=\"col\">Fecha</th>", mine);
        Assert.Contains("<th scope=\"col\">Hora</th>", mine);
        Assert.Contains("<th scope=\"col\">Cancha</th>", mine);
        Assert.Contains("<th scope=\"col\">Precio</th>", mine);
        Assert.Contains("<th scope=\"col\">Estado</th>", mine);
        Assert.Contains("<th scope=\"col\">Acciones</th>", mine);
        Assert.Contains("mine-tabla-scroll", mine);
        Assert.Contains("Activa", mine);
        Assert.DoesNotContain(">Active<", mine);
        Assert.Contains("mine-actions", mine);
        Assert.Contains("btn btn-primary btn-sm", mine);
        Assert.Contains("btn btn-outline-danger btn-sm", mine);
        Assert.DoesNotContain("btn-link", mine);
        Assert.Contains("Ver detalle", mine);
        Assert.Contains("Cancelar reserva", mine);
        Assert.Contains("__RequestVerificationToken", mine);
        Assert.DoesNotContain("list-group", mine);

        using var other = await Login(app, "tabla-otro@example.test");
        var otherMine = await other.GetStringAsync("/Bookings/Mine");
        Assert.DoesNotContain("Cancha Tabla", otherMine);

        var bookingId = await BookingId(app, "tabla@example.test", courtId, date);
        var cancel = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Cancel");
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        var after = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.Contains("Cancelada", after);
        Assert.DoesNotContain("Cancelar reserva", after);
        Assert.Contains("Cancha Tabla", after);
    }

    [Fact]
    public async Task Resultados_sin_fecha_devuelve_estado_canonico_sin_layout()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync("/Reservas/Resultados");
        Assert.Contains("Elegí una fecha del calendario", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<html", html);
        Assert.DoesNotContain("Reservar turno", html);
    }

    [Fact]
    public async Task Limpiar_usa_partial_canonico_actualiza_calendario_y_url_reservas()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync("/Reservas");
        Assert.Contains("fetch('/Reservas/Resultados',", html);
        Assert.Contains("marcarCalendario([], null)", html);
        Assert.Contains("ir('/Reservas', null)", html);
        Assert.Contains("ir(url, fecha)", html);
        Assert.DoesNotContain("window.location.href", html);
    }

    [Fact]
    public async Task Calendario_y_fallback_se_sincronizan_al_navegar_y_volver()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync("/Reservas");
        Assert.Contains("marcarCalendario([fecha], fecha)", html);
        Assert.Contains("selectedMonth", html);
        Assert.Contains("parseInt(p[1], 10) - 1", html);
        Assert.Contains("syncFallback(fecha)", html);
        Assert.Contains("syncFallback('')", html);
        Assert.Contains("getElementById('fecha')", html);
        Assert.Contains("if (sync) return;", html);
    }

    [Fact]
    public async Task Fetch_usa_cache_no_store_y_retry_no_duplica_historial()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await guest.GetStringAsync("/Reservas");
        Assert.Contains("cache: 'no-store'", html);
        Assert.Contains("location.search", html);
    }

    [Fact]
    public async Task Mine_acciones_ver_detalle_anchor_boton_y_cancelar_post_sin_cambios()
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Cancha Botones");
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "botones@example.test");
        await SeedBooking(app, "botones@example.test", courtId, date);
        var mine = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        var bookingId = await BookingId(app, "botones@example.test", courtId, date);
        Assert.Contains($"<a class=\"btn btn-primary btn-sm\" href=\"/Bookings/{bookingId}\">Ver detalle</a>", mine);
        Assert.Contains($"action=\"/Bookings/{bookingId}/Cancel\"", mine);
        Assert.Contains("method=\"post\"", mine);
        Assert.Contains("__RequestVerificationToken", mine);
    }

    [Fact]
    public async Task Mine_muestra_precio_persistido_por_fila_no_tarifa_global()
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Cancha Precio");
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "precio@example.test");
        await SeedBooking(app, "precio@example.test", courtId, date);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={app.Database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Bookings SET PriceArs = 25000";
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        var mine = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.Contains("<td>ARS 25.000</td>", mine);
        Assert.DoesNotContain("<td>ARS 40.000</td>", mine);
    }

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    private static async Task<int> CreateCourtWithMondaySchedule(TestApp app, string name)
    {
        using var scope = app.Services.CreateScope();
        var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
        await courts.CreateAsync(new CourtForm { Name = name, Description = "t", IsActive = true });
        var courtId = (await courts.ListAsync()).Single(c => c.Name == name).Id;
        await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
            .CreateScheduleAsync(courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        return courtId;
    }

    private static async Task<int> BookingId(TestApp app, string email, int courtId, DateOnly date)
    {
        string userId;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
            userId = (await users.FindByEmailAsync(email))!.Id;
        }
        return Convert.ToInt32(app.Scalar(
            $"SELECT Id FROM Bookings WHERE CourtId = {courtId} AND Date = '{date:yyyy-MM-dd}' AND UserId = '{userId}'"));
    }

    // La reserva solo nace con pago total: en tests se siembra directo vía
    // servicio (el POST /Bookings/Create siempre devuelve 400).
    private static async Task<int> SeedBooking(TestApp app, string email, int courtId, DateOnly date)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
        var userId = (await users.FindByEmailAsync(email))!.Id;
        return (await scope.ServiceProvider.GetRequiredService<BookingService>()
            .CreateAsync(userId, courtId, date, new TimeOnly(9, 0))).Id;
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
}
