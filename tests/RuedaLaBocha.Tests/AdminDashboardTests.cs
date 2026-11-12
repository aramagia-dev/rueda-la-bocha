using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class AdminDashboardTests
{
    [Fact]
    public async Task Totales_dia_mes_anio_con_ocupacion_y_desglose()
    {
        using var app = new TestApp();
        var fixture = await Seed(app);
        using var admin = await app.Login(admin: true);

        var html = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync($"/Admin/Dashboard?date={fixture.Monday:yyyy-MM-dd}"));
        Assert.Contains("RECAUDACIÓN DEL DÍA</p><h2>ARS 80.000</h2>", html); // 2 activas del lunes x 40000
        var expectedMonth = fixture.Tuesday.Month == fixture.Monday.Month ? "120.000" : "80.000";
        Assert.Contains($"RECAUDACIÓN DEL MES</p><h2>ARS {expectedMonth}</h2>", html);
        Assert.Contains("RECAUDACIÓN DEL AÑO</p><h2>ARS 120.007</h2>", html); // 3 x 40000 del año + 7 de otro mes
        Assert.Contains("66 %", html); // ocupación: 2 de 3 turnos
        Assert.Contains("2 de 3 turnos ocupados", html);
        Assert.Contains("Panel court A", html);
        Assert.Contains("Panel court B", html);
        Assert.Contains(fixture.UserA, html);
        Assert.DoesNotContain(fixture.UserB, html);
        Assert.Contains("11:00", html); // cancelada del día también se lista
        Assert.Contains("Cancelled", html);
        Assert.Contains("vc-calendario", html);
        Assert.Contains("vanilla-calendar-pro.js", html);
        Assert.Contains("/Courts/Manage", html);
        Assert.Contains(">Panel<", html);

        var tuesday = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync($"/Admin/Dashboard?date={fixture.Tuesday:yyyy-MM-dd}"));
        Assert.Contains(fixture.UserB, tuesday);
        Assert.DoesNotContain(fixture.UserA, tuesday);
    }

    [Fact]
    public async Task Panel_tarjeta_pagos_y_css_contiene_el_desborde_movil()
    {
        using var app = new TestApp();
        await Seed(app);
        using var admin = await app.Login(admin: true);
        var html = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Dashboard"));
        Assert.Contains("PAGOS PENDIENTES", html);
        Assert.DoesNotContain("SEÑALES", html);
        Assert.Contains("management-panel", html);
        Assert.Contains("table-responsive", html);
        using var guest = app.Client();
        var css = await guest.GetStringAsync("/css/site.css");
        Assert.Contains(".e1-layout > *", css);
        Assert.Contains("#panel-dia", css);
        Assert.Contains(".management-panel > .table-responsive", css);
    }

    [Fact]
    public async Task Fecha_invalida_muestra_hoy()
    {
        using var app = new TestApp();
        await Seed(app);
        using var admin = await app.Login(admin: true);

        var response = await admin.GetAsync("/Admin/Dashboard?date=no-fecha");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Fecha inválida, se muestra hoy.", html);
    }

    [Fact]
    public async Task Autorizacion_como_el_resto_del_admin_y_crud_vivo()
    {
        using var app = new TestApp();
        await Seed(app);
        using var admin = await app.Login(admin: true);
        using var customer = await LoginCustomer(app, "panel-customer@example.test");
        using var guest = app.Client();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin/Dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Courts/Manage")).StatusCode);

        var guestPanel = await guest.GetAsync("/Admin/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, guestPanel.StatusCode);
        Assert.Contains("Login", guestPanel.Headers.Location!.ToString());

        var customerPanel = await customer.GetAsync("/Admin/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, customerPanel.StatusCode);
        Assert.Contains("AccessDenied", customerPanel.Headers.Location!.ToString());

        var guestHome = await guest.GetStringAsync("/Reservas");
        Assert.DoesNotContain(">Panel<", guestHome);
    }

    private sealed record Fixture(int CourtA, int CourtB, DateOnly Monday, DateOnly Tuesday, string UserA, string UserB);

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    private static async Task<Fixture> Seed(TestApp app)
    {
        var monday = Next(DayOfWeek.Monday);
        var tuesday = monday.AddDays(1);
        int courtA, courtB;
        string userA, userB;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Panel court A", Description = "t", IsActive = true });
            await courts.CreateAsync(new CourtForm { Name = "Panel court B", Description = "t", IsActive = true });
            courtA = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == "Panel court A").Id;
            courtB = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == "Panel court B").Id;
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            await schedules.CreateScheduleAsync(courtA, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0));
            await schedules.CreateScheduleAsync(courtB, DayOfWeek.Tuesday, new TimeOnly(11, 0), new TimeOnly(13, 0));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            userA = await CreateUser(users, "panel-a@example.test");
            userB = await CreateUser(users, "panel-b@example.test");
        }
        using (var scope = app.Services.CreateScope())
        {
            var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
            await bookings.CreateAsync(userA, courtA, monday, new TimeOnly(9, 0));
            await bookings.CreateAsync(userB, courtB, tuesday, new TimeOnly(11, 0));
            var cancelled = await bookings.CreateAsync(userA, courtA, monday, new TimeOnly(11, 0));
            await bookings.CancelAsync(cancelled.Id, userA, isAdmin: false);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherMonth = monday.AddMonths(monday.Month == 12 ? -1 : 1);
            db.Bookings.Add(new Booking
            {
                CourtId = courtA,
                UserId = userA,
                Date = new DateOnly(otherMonth.Year, otherMonth.Month, 10),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                PriceArs = 7m,
                Status = BookingStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            });
            db.Bookings.Add(new Booking
            {
                CourtId = courtA,
                UserId = userA,
                Date = monday,
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(11, 0),
                PriceArs = BookingPolicy.PriceArs,
                Status = BookingStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            });
            db.BookingHolds.Add(new BookingHold
            {
                CourtId = courtB,
                UserId = userB,
                Date = tuesday,
                StartTime = new TimeOnly(12, 0),
                EndTime = new TimeOnly(13, 0),
                AmountArs = BookingPolicy.PriceArs,
                Status = BookingHoldStatus.Pending,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            });
            db.BookingHolds.Add(new BookingHold
            {
                CourtId = courtA,
                UserId = userA,
                Date = monday,
                StartTime = new TimeOnly(11, 0),
                EndTime = new TimeOnly(12, 0),
                AmountArs = BookingPolicy.PriceArs,
                Status = BookingHoldStatus.Pending,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }
        return new Fixture(courtA, courtB, monday, tuesday, userA, userB);
    }

    private static async Task<string> CreateUser(UserManager<IdentityUser> users, string email)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        return user.Id;
    }

    private static async Task<HttpClient> LoginCustomer(TestApp app, string email)
    {
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.CreateAsync(
                new IdentityUser { UserName = email, Email = email, EmailConfirmed = true }, TestApp.Password)).Succeeded);
        }
        var client = app.Client();
        var response = await CourtFlowTests.Post(client, "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
}
