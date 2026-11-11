using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

// Demo Padi sandbox: enlace SingleUse en ARS desde reserva propia activa,
// aprobado solo ante Succeeded + mismo enlace + importe + moneda.
[Collection("Court integration")]
public sealed class PadiDemoTests
{
    private sealed class FakePadi : IPadiClient
    {
        public PadiLink Link { get; set; } = new("link-1", "https://padi.test/checkout/link-1", "https://padi.test/qr/link-1", "Active");
        public List<PadiReceivedPayment> Received { get; } = new();
        public Dictionary<string, PadiReceivedPayment> ById { get; } = new();

        public Task<PadiLink> CreateLinkAsync(decimal amountArs, string title, string? description, CancellationToken ct = default)
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
    public async Task Dueno_crea_enlace_y_detalle_muestra_enlace_y_qr()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Padi court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-owner@example.test");

        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings")).StatusCode);
        var bookingId = await BookingId(app, "padi-owner@example.test", courtId, date);

        var fresh = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("Pagar con Padi (demo)", fresh);

        var created = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Padi");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("https://padi.test/checkout/link-1", detail);
        Assert.Contains("https://padi.test/qr/link-1", detail);
        Assert.Contains("Comprobar estado", detail);
        Assert.Contains("Pendiente", detail);
        Assert.Contains("target=\"_blank\"", await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM PadiPayments"));
    }

    [Fact]
    public async Task Ajeno_no_puede_crear_ni_consultar()
    {
        using var app = new PadiTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Padi ajeno court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "padi-dueno@example.test");
        using var other = await Login(app, "padi-otro@example.test");

        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings")).StatusCode);
        var bookingId = await BookingId(app, "padi-dueno@example.test", courtId, date);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await CourtFlowTests.Post(other, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Padi")).StatusCode);
        var statusResponse = await other.GetAsync($"/Bookings/{bookingId}/Padi/Status");
        Assert.Equal(HttpStatusCode.Forbidden, statusResponse.StatusCode);
        Assert.Contains("propias reservas", await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM PadiPayments"));
    }

    [Fact]
    public async Task Pendiente_nunca_se_muestra_aprobado()
    {
        using var app = new PadiTestApp();
        app.Fake.Received.Add(new("pay-pend", "link-1", 40_000m, "ARS", "Pending"));
        app.Fake.ById["pay-pend"] = new("pay-pend", "link-1", 40_000m, "ARS", "Pending");
        var bookingId = await SeedBookingWithLink(app, "padi-pend@example.test");

        using var owner = app.Client();
        await LoginInto(owner, "padi-pend@example.test");
        var status = await owner.GetAsync($"/Bookings/{bookingId}/Padi/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains(">Pendiente</strong>", detail);
        Assert.DoesNotContain("Pago aprobado", detail);
        Assert.Equal(0L, app.Scalar("SELECT Status FROM PadiPayments"));
    }

    [Theory]
    [InlineData(1000.0, "ARS")]
    [InlineData(40000.0, "USD")]
    public async Task Importe_o_moneda_distintos_no_aprueban(double amountValue, string currency)
    {
        var amount = Convert.ToDecimal(amountValue);
        using var app = new PadiTestApp();
        app.Fake.Received.Add(new("pay-mal", "link-1", amount, currency, "Succeeded"));
        app.Fake.ById["pay-mal"] = new("pay-mal", "link-1", amount, currency, "Succeeded");
        var bookingId = await SeedBookingWithLink(app, $"padi-mal-{amount}-{currency}@example.test");

        using var owner = app.Client();
        await LoginInto(owner, $"padi-mal-{amount}-{currency}@example.test");
        var status = await owner.GetAsync($"/Bookings/{bookingId}/Padi/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains(">Pendiente</strong>", detail);
        Assert.DoesNotContain("Pago aprobado", detail);
        Assert.Equal(0L, app.Scalar("SELECT Status FROM PadiPayments"));
    }

    [Fact]
    public async Task Succeeded_con_mismo_enlace_importe_y_moneda_aprueba()
    {
        using var app = new PadiTestApp();
        app.Fake.Received.Add(new("pay-ok", "link-1", 40_000m, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", 40_000m, "ARS", "Succeeded");
        var bookingId = await SeedBookingWithLink(app, "padi-ok@example.test");

        using var owner = app.Client();
        await LoginInto(owner, "padi-ok@example.test");
        var status = await owner.GetAsync($"/Bookings/{bookingId}/Padi/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("approved").GetBoolean());

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("Aprobado", detail);
        Assert.Equal(2L, app.Scalar("SELECT Status FROM PadiPayments"));
    }

    private static async Task<int> SeedBookingWithLink(PadiTestApp app, string email)
    {
        var courtId = await CreateCourtWithMondaySchedule(app, $"Padi {email}", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, email);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings")).StatusCode);
        var bookingId = await BookingId(app, email, courtId, date);
        Assert.Equal(HttpStatusCode.Redirect,
            (await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Padi")).StatusCode);
        return bookingId;
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
