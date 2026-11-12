using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

// Pago TOTAL con Padi: la reserva solo nace del hold consumido. El POST
// directo /Bookings/Create devuelve 400 y el POST /Bookings/{id}/Padi se
// rechaza (anti doble cobro): la reserva Active ya tiene pago total o es
// legacy sin cobro habilitado.
[Collection("Court integration")]
public sealed class PadiDemoTests
{
    private sealed class FakePadi : IPadiClient
    {
        public PadiLink Link { get; set; } = new("link-1", "https://padi.test/checkout/link-1", "https://padi.test/qr/link-1", "Active");
        public List<PadiReceivedPayment> Received { get; } = new();
        public Dictionary<string, PadiReceivedPayment> ById { get; } = new();

        public Task<PadiLink> CreateLinkAsync(decimal amountArs, string title, CancellationToken ct = default, int? ttlMinutes = null)
            => Task.FromResult(Link);

        public Task<IReadOnlyList<PadiReceivedPayment>> ListReceivedPaymentsAsync(int limit = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PadiReceivedPayment>>(Received);

        public Task<PadiReceivedPayment?> GetPaymentAsync(string paymentId, CancellationToken ct = default)
            => Task.FromResult(ById.TryGetValue(paymentId, out var found) ? found : null);
    }

    private sealed class PadiTestApp : TestApp
    {
        public readonly FakePadi Fake = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            var fake = Fake;
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPadiClient>();
                services.AddSingleton<IPadiClient>(fake);
            });
        }
    }

    [Fact]
    public async Task PadiCreate_sobre_Activa_se_rechaza_sin_doble_cobro()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Padi court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-owner@example.test");
        await SeedBooking(app, "padi-owner@example.test", courtId, date);
        var bookingId = await BookingId(app, "padi-owner@example.test", courtId, date);

        var created = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Padi");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Contains($"/Bookings/{bookingId}", created.Headers.Location!.ToString());
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM PadiPayments"));

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("ya tiene pago total", detail);
        Assert.DoesNotContain("Pagar con Padi", detail);
    }

    [Fact]
    public async Task PadiCreate_con_pago_Succeeded_tambien_se_rechaza()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Padi succeeded", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-succ@example.test");
        var bookingId = await SeedBooking(app, "padi-succ@example.test", courtId, date);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RuedaLaBocha.Web.Data.ApplicationDbContext>();
            db.PadiPayments.Add(new PadiPayment
            {
                BookingId = bookingId,
                PadiLinkId = "link-1",
                PadiPaymentId = "pay-ok",
                AmountArs = BookingPolicy.PriceArs,
                Currency = "ARS",
                Status = PadiPaymentStatus.Succeeded,
            });
            await db.SaveChangesAsync();
        }

        var created = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Padi");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM PadiPayments"));

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("ya tiene pago total", detail);
    }

    [Fact]
    public async Task Detail_nacida_de_hold_muestra_Pagado_sin_boton_ni_qr()
    {
        using var app = new PadiTestApp();
        app.Fake.Received.Add(new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded");
        var courtId = await CreateCourtWithMondaySchedule(app, "Padi hold", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-hold@example.test");

        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Mine",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings/Holds")).StatusCode);
        var holdId = Convert.ToInt32(app.Scalar("SELECT Id FROM BookingHolds"));
        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        int bookingId;
        using (var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("approved").GetBoolean());
            bookingId = doc.RootElement.GetProperty("bookingId").GetInt32();
        }

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("Pagado", detail);
        Assert.Contains("pay-ok", detail);
        Assert.Contains("Comprobante en Padi", detail);
        Assert.Contains("https://padi.test/checkout/link-1", detail);
        Assert.DoesNotContain("Pagar con Padi", detail);
        Assert.DoesNotContain("Comprobar estado", detail);
        Assert.DoesNotContain("qr/link-1", detail);
    }

    [Fact]
    public async Task Detail_legacy_Active_sin_hold_no_ofrece_pago()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Legacy court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-legacy@example.test");
        var bookingId = await SeedBooking(app, "padi-legacy@example.test", courtId, date);

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("no requiere ni admite otro cobro", detail);
        Assert.DoesNotContain("Padi", detail);
        Assert.DoesNotContain("Comprobar estado", detail);
    }

    [Fact]
    public async Task Detail_Cancelled_sin_seccion_de_pago()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Cancel court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-cancel@example.test");
        var bookingId = await SeedBooking(app, "padi-cancel@example.test", courtId, date);
        Assert.Equal(HttpStatusCode.Redirect,
            (await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Cancel")).StatusCode);

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.DoesNotContain("Padi", detail);
        Assert.DoesNotContain("Cancelar reserva", detail);
    }

    [Fact]
    public async Task Ajeno_no_puede_crear_ni_consultar()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Padi ajeno court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-dueno@example.test");
        using var other = await Login(app, "padi-otro@example.test");
        var bookingId = await SeedBooking(app, "padi-dueno@example.test", courtId, date);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await CourtFlowTests.Post(other, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Padi")).StatusCode);
        var statusResponse = await other.GetAsync($"/Bookings/{bookingId}/Padi/Status");
        Assert.Equal(HttpStatusCode.Forbidden, statusResponse.StatusCode);
        Assert.Contains("propias reservas", await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM PadiPayments"));
    }

    [Fact]
    public async Task Mine_filtra_por_status_con_enlaces()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Padi filtro", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-filtro@example.test");
        var activeId = await SeedBookingAt(app, "padi-filtro@example.test", courtId, date, new TimeOnly(9, 0));
        var cancelledId = await SeedBookingAt(app, "padi-filtro@example.test", courtId, date, new TimeOnly(11, 0));
        Assert.Equal(HttpStatusCode.Redirect,
            (await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{cancelledId}/Cancel")).StatusCode);
        _ = activeId;

        var todas = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.Contains("09:00", todas);
        Assert.Contains("11:00", todas);
        Assert.Contains(">Todas<", todas);
        Assert.Contains("href=\"/Bookings/Mine?status=Active\"", todas);
        Assert.Contains("href=\"/Bookings/Mine?status=Cancelled\"", todas);
        Assert.Equal(1, todas.Split("btn btn-sm btn-primary").Length - 1);

        var activas = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine?status=Active"));
        Assert.Contains("09:00", activas);
        Assert.DoesNotContain("11:00", activas);
        Assert.Contains("Activa", activas);
        Assert.Equal(1, activas.Split("btn btn-sm btn-primary").Length - 1);
        Assert.Contains(">Activas<", activas);

        var canceladas = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine?status=Cancelled"));
        Assert.Contains("11:00", canceladas);
        Assert.DoesNotContain("09:00", canceladas);
        Assert.Contains("Cancelada", canceladas);
        Assert.Equal(1, canceladas.Split("btn btn-sm btn-primary").Length - 1);
        Assert.Contains(">Canceladas<", canceladas);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/Bookings/Mine?status=Bogus")).StatusCode);
    }

    private static async Task<int> SeedBooking(TestApp app, string email, int courtId, DateOnly date) =>
        await SeedBookingAt(app, email, courtId, date, new TimeOnly(9, 0));

    private static async Task<int> SeedBookingAt(TestApp app, string email, int courtId, DateOnly date, TimeOnly start)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var userId = (await users.FindByEmailAsync(email))!.Id;
        return (await scope.ServiceProvider.GetRequiredService<BookingService>()
            .CreateAsync(userId, courtId, date, start)).Id;
    }

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    private static async Task<int> CreateCourtWithMondaySchedule(TestApp app, string name, TimeOnly start, TimeOnly end)
    {
        using var scope = app.Services.CreateScope();
        var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
        await courts.CreateAsync(new CourtForm { Name = name, Description = "Test court", IsActive = true });
        var courtId = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == name).Id;
        await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
            .CreateScheduleAsync(courtId, DayOfWeek.Monday, start, end);
        return courtId;
    }

    private static async Task<int> BookingId(TestApp app, string email, int courtId, DateOnly date)
    {
        string userId;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            userId = (await users.FindByEmailAsync(email))!.Id;
        }
        return Convert.ToInt32(app.Scalar(
            $"SELECT Id FROM Bookings WHERE CourtId = {courtId} AND Date = '{date:yyyy-MM-dd}' AND UserId = '{userId}'"));
    }

    private static async Task<HttpClient> Login(TestApp app, string email)
    {
        var client = app.Client();
        await LoginInto(client, email, app);
        return client;
    }

    private static async Task LoginInto(HttpClient client, string email, TestApp? app = null)
    {
        if (app is not null)
        {
            using var scope = app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        }
        var response = await CourtFlowTests.Post(client, "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
