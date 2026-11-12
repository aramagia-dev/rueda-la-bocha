using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

// B4: /Bookings/Mine muestra los holds Pending vigentes del usuario con
// retomar pago; ?status=Pending los filtra. Sin pagos reales (FakePadi).
[Collection("Court integration")]
public sealed class BookingPendingHoldsMineTests
{
    private sealed class FakePadi : IPadiClient
    {
        public Task<PadiLink> CreateLinkAsync(decimal amountArs, string title, CancellationToken ct = default, int? ttlMinutes = null)
            => Task.FromResult(new PadiLink("link-1", "https://padi.test/checkout/link-1", "https://padi.test/qr/link-1", "Active"));

        public Task<IReadOnlyList<PadiReceivedPayment>> ListReceivedPaymentsAsync(int limit = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PadiReceivedPayment>>(Array.Empty<PadiReceivedPayment>());

        public Task<PadiReceivedPayment?> GetPaymentAsync(string paymentId, CancellationToken ct = default)
            => Task.FromResult<PadiReceivedPayment?>(null);
    }

    private sealed class HoldTestApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPadiClient>();
                services.AddSingleton<IPadiClient>(new FakePadi());
            });
        }
    }

    [Fact]
    public async Task Mine_muestra_hold_vigente_propio_con_retomar()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Mine hold", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "mine-hold@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "mine-hold@example.test");

        var mine = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.Contains("Pendiente de pago", mine);
        Assert.Contains("Retomar pago", mine);
        Assert.Contains($"/Bookings/Holds/{holdId}", mine);
        Assert.Contains("Mine hold", mine);
        Assert.Contains("ARS", mine);
    }

    [Fact]
    public async Task Mine_no_muestra_hold_ajeno()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Mine ajeno", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "mine-dueno@example.test");
        using var other = await Login(app, "mine-otro@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "mine-dueno@example.test");

        var mine = WebUtility.HtmlDecode(await other.GetStringAsync("/Bookings/Mine"));
        Assert.DoesNotContain($"/Bookings/Holds/{holdId}", mine);
        Assert.DoesNotContain("Retomar pago", mine);
    }

    [Fact]
    public async Task Mine_excluye_hold_vencido_y_slot_libre()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Mine vence", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "mine-vence@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "mine-vence@example.test");
        ExpireHold(app, holdId);

        var mine = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.DoesNotContain($"/Bookings/Holds/{holdId}", mine);
        var resultados = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Reservas/Resultados?fecha={date:yyyy-MM-dd}"));
        Assert.Contains("09:00", resultados);
    }

    [Fact]
    public async Task Status_Pending_filtra_solo_holds_y_convive_con_resto()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Mine filtro", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "mine-filtro@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "mine-filtro@example.test");
        await SeedBooking(app, "mine-filtro@example.test", courtId, date, new TimeOnly(10, 0));

        var pending = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine?status=Pending"));
        Assert.Contains($"/Bookings/Holds/{holdId}", pending);
        Assert.Contains("Retomar pago", pending);
        Assert.DoesNotContain("Ver detalle", pending);

        var todas = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.Contains($"/Bookings/Holds/{holdId}", todas);
        Assert.Contains("Ver detalle", todas);

        var activas = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine?status=Active"));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/Bookings/Mine?status=Active")).StatusCode);
        Assert.Contains("Ver detalle", activas);
    }

    [Fact]
    public async Task Retomar_lleva_al_hold_vigente()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Mine retomar", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "mine-retomar@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "mine-retomar@example.test");

        var mine = WebUtility.HtmlDecode(await owner.GetStringAsync("/Bookings/Mine"));
        Assert.Contains($"/Bookings/Holds/{holdId}", mine);
        var detail = await owner.GetAsync($"/Bookings/Holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        Assert.Contains("Pendiente de pago", body);
        Assert.Contains("Comprobar estado", body);
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

    private static async Task<HttpClient> Login(TestApp app, string email)
    {
        var client = app.Client();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        }
        var response = await CourtFlowTests.Post(client, "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<int> CreateHold(HoldTestApp app, HttpClient owner, int courtId, DateOnly date, string email)
    {
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Mine",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings/Holds")).StatusCode);
        string userId;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            userId = (await users.FindByEmailAsync(email))!.Id;
        }
        return Convert.ToInt32(app.Scalar(
            $"SELECT Id FROM BookingHolds WHERE CourtId = {courtId} AND Date = '{date:yyyy-MM-dd}' AND UserId = '{userId}'"));
    }

    private static void ExpireHold(HoldTestApp app, int holdId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hold = db.BookingHolds.Single(h => h.Id == holdId);
        hold.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        db.SaveChanges();
    }

    private static async Task SeedBooking(TestApp app, string email, int courtId, DateOnly date, TimeOnly start)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var userId = (await users.FindByEmailAsync(email))!.Id;
        await scope.ServiceProvider.GetRequiredService<BookingService>()
            .CreateAsync(userId, courtId, date, start);
    }
}
