using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class BookingOwnerFlowTests
{
    [Fact]
    public async Task Post_directo_se_rechaza_sin_crear_y_dueno_gestiona_las_pagas()
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Owner court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "owner@example.test");

        // B3: la reserva se crea solo con pago total (holds + Padi).
        var before = app.Snapshot();
        var direct = await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings");
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
        Assert.Contains("pago total", await direct.Content.ReadAsStringAsync());
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(before, app.Snapshot());

        await SeedBooking(app, "owner@example.test", courtId, date);
        var mine = await owner.GetStringAsync("/Bookings/Mine");
        Assert.Contains("09:00", mine);
        Assert.Contains(BookingPolicy.PriceDisplay, mine);

        var bookingId = await BookingId(app, "owner@example.test", courtId, date);
        var detail = await owner.GetAsync($"/Bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var cancel = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Cancel");
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);

        var repeat = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Cancel");
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Contains("ya estaba cancelada", await repeat.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Non_owner_cannot_read_or_cancel_and_unknown_id_is_not_found()
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Isolated court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "owner-a@example.test");
        using var other = await Login(app, "owner-b@example.test");
        await SeedBooking(app, "owner-a@example.test", courtId, date);
        var bookingId = await BookingId(app, "owner-a@example.test", courtId, date);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/Bookings/{bookingId}")).StatusCode);
        Assert.Contains("propias reservas", await (await other.GetAsync($"/Bookings/{bookingId}")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await CourtFlowTests.Post(other, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Cancel")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/Bookings/999999")).StatusCode);

        var otherMine = await other.GetStringAsync("/Bookings/Mine");
        Assert.DoesNotContain("09:00", otherMine);
    }

    [Fact]
    public async Task Anonymous_is_challenged_on_owner_routes()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        Assert.Equal(HttpStatusCode.Redirect, (await guest.GetAsync("/Bookings/Mine")).StatusCode);
        Assert.Contains("Login", (await guest.GetAsync("/Bookings/Mine")).Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("past")]
    [InlineData("inactive")]
    [InlineData("unknown")]
    [InlineData("closure")]
    public async Task Owner_create_rejections_carry_status_and_spanish_message(string kind)
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, $"Reject {kind} court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        if (kind == "closure") await AddClosure(app, courtId, date, new TimeOnly(9, 0), new TimeOnly(10, 0));
        if (kind == "inactive") await Deactivate(app, courtId);
        if (kind == "past") date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        if (kind == "unknown") courtId = 999_999;
        using var owner = await Login(app, $"reject-{kind}@example.test");

        // B3: cualquier POST directo se rechaza con el mensaje de pago
        // total, sin importar la validez del slot.
        var before = app.Snapshot();
        var response = await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("pago total", await response.Content.ReadAsStringAsync());
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(before, app.Snapshot());
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

    private static async Task AddClosure(TestApp app, int courtId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
            .CreateClosureAsync(courtId, date, start, end);
    }

    private static async Task Deactivate(TestApp app, int courtId)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CourtService>().DeactivateAsync(courtId);
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

    // La reserva solo nace con pago total: en tests se siembra directo vía
    // servicio (el POST /Bookings/Create siempre devuelve 400).
    private static async Task<int> SeedBooking(TestApp app, string email, int courtId, DateOnly date)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var userId = (await users.FindByEmailAsync(email))!.Id;
        return (await scope.ServiceProvider.GetRequiredService<BookingService>()
            .CreateAsync(userId, courtId, date, new TimeOnly(9, 0))).Id;
    }

    private static async Task<HttpClient> Login(TestApp app, string email)
    {
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        }
        var client = app.Client();
        var response = await CourtFlowTests.Post(client, "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
}
