using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class BookingAdminFlowTests
{
    [Fact]
    public async Task Admin_list_filters_combine_and_empty_result_is_200()
    {
        using var app = new TestApp();
        var fixture = await Seed(app);
        using var admin = await LoginAdmin(app);

        var all = await admin.GetStringAsync("/Admin/Bookings");
        Assert.Contains("09:00", all);
        Assert.Contains("11:00", all);

        var byDate = await admin.GetStringAsync($"/Admin/Bookings?date={fixture.Monday:yyyy-MM-dd}");
        Assert.Contains("09:00", byDate);
        Assert.DoesNotContain("11:00", byDate);

        var byCourt = await admin.GetStringAsync($"/Admin/Bookings?courtId={fixture.CourtB}");
        Assert.DoesNotContain("09:00", byCourt);
        Assert.Contains("11:00", byCourt);

        var byUser = await admin.GetStringAsync($"/Admin/Bookings?userId={fixture.UserA}");
        Assert.Contains("09:00", byUser);
        Assert.DoesNotContain("11:00", byUser);

        var combined = await admin.GetStringAsync(
            $"/Admin/Bookings?date={fixture.Monday:yyyy-MM-dd}&courtId={fixture.CourtA}&status=Active&userId={fixture.UserA}");
        Assert.Contains("09:00", combined);

        var empty = await admin.GetAsync("/Admin/Bookings?courtId=999999");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.DoesNotContain("09:00", await empty.Content.ReadAsStringAsync());

        var badStatus = await admin.GetAsync("/Admin/Bookings?status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

    [Fact]
    public async Task Admin_auth_matrix_challenges_anonymous_and_denies_customer()
    {
        using var app = new TestApp();
        await Seed(app);
        using var admin = await LoginAdmin(app);
        using var customer = await LoginCustomer(app, "customer-matrix@example.test");
        using var guest = app.Client();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin/Bookings")).StatusCode);

        var guestList = await guest.GetAsync("/Admin/Bookings");
        Assert.Equal(HttpStatusCode.Redirect, guestList.StatusCode);
        Assert.Contains("Login", guestList.Headers.Location!.ToString());

        var customerList = await customer.GetAsync("/Admin/Bookings");
        Assert.Equal(HttpStatusCode.Redirect, customerList.StatusCode);
        Assert.Contains("AccessDenied", customerList.Headers.Location!.ToString());

        var customerCancel = await CourtFlowTests.Post(customer, "/Bookings/Create", new(), "/Admin/Bookings/1/Cancel");
        Assert.Equal(HttpStatusCode.Redirect, customerCancel.StatusCode);
        Assert.Contains("AccessDenied", customerCancel.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_cancels_any_row_idempotently()
    {
        using var app = new TestApp();
        var fixture = await Seed(app);
        using var admin = await LoginAdmin(app);

        var cancel = await CourtFlowTests.Post(admin, "/Admin/Bookings", new(), $"/Admin/Bookings/{fixture.BookingA}/Cancel");
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Equal(1L, app.Scalar($"SELECT Status FROM Bookings WHERE Id = {fixture.BookingA}"));

        var repeat = await CourtFlowTests.Post(admin, "/Courts/Create", new(), $"/Admin/Bookings/{fixture.BookingA}/Cancel");
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Contains("ya estaba cancelada", await repeat.Content.ReadAsStringAsync());

        var unknown = await CourtFlowTests.Post(admin, "/Courts/Create", new(), "/Admin/Bookings/999999/Cancel");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private sealed record Fixture(int CourtA, int CourtB, DateOnly Monday, string UserA, int BookingA);

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    private static async Task<Fixture> Seed(TestApp app)
    {
        var monday = Next(DayOfWeek.Monday);
        int courtA, courtB;
        string userA, userB;
        using (var scope = app.Services.CreateScope())
        {
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Admin court A", Description = "t", IsActive = true });
            await courts.CreateAsync(new CourtForm { Name = "Admin court B", Description = "t", IsActive = true });
            courtA = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == "Admin court A").Id;
            courtB = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == "Admin court B").Id;
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            await schedules.CreateScheduleAsync(courtA, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0));
            await schedules.CreateScheduleAsync(courtB, DayOfWeek.Tuesday, new TimeOnly(11, 0), new TimeOnly(13, 0));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            userA = await CreateUser(users, "admin-a@example.test");
            userB = await CreateUser(users, "admin-b@example.test");
        }
        int bookingA;
        using (var scope = app.Services.CreateScope())
        {
            var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
            bookingA = (await bookings.CreateAsync(userA, courtA, monday, new TimeOnly(9, 0))).Id;
            var tuesday = monday.AddDays(1);
            await bookings.CreateAsync(userB, courtB, tuesday, new TimeOnly(11, 0));
        }
        return new Fixture(courtA, courtB, monday, userA, bookingA);
    }

    private static async Task<string> CreateUser(UserManager<IdentityUser> users, string email)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        return user.Id;
    }

    private static async Task<HttpClient> LoginAdmin(TestApp app) => await app.Login(admin: true);

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
