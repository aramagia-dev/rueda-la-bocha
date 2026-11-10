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
    public async Task Owner_create_lists_and_cancels_with_spanish_copy()
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Owner court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "owner@example.test");

        var created = await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var mine = await owner.GetStringAsync("/Bookings/Mine");
        Assert.Contains("09:00", mine);
        Assert.Contains("40.000", mine);

        var bookingId = await BookingId(app, "owner@example.test", courtId, date);
        var detail = await owner.GetAsync($"/Bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var cancel = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/{bookingId}/Cancel");
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);

        var repeat = await CourtFlowTests.Post(owner, "/Bookings/Create", new(), $"/Bookings/{bookingId}/Cancel");
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

        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings")).StatusCode);
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
    [InlineData("past", 400, "pasado")]
    [InlineData("inactive", 400, "no está disponible")]
    [InlineData("unknown", 404, "no existe")]
    [InlineData("closure", 400, "bloqueo")]
    public async Task Owner_create_rejections_carry_status_and_spanish_message(string kind, int status, string fragment)
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, $"Reject {kind} court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        if (kind == "closure") await AddClosure(app, courtId, date, new TimeOnly(9, 0), new TimeOnly(10, 0));
        if (kind == "inactive") await Deactivate(app, courtId);
        if (kind == "past") date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        if (kind == "unknown") courtId = 999_999;
        using var owner = await Login(app, $"reject-{kind}@example.test");

        var response = await CourtFlowTests.Post(owner, "/Bookings/Create",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Contains(fragment, await response.Content.ReadAsStringAsync());
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
