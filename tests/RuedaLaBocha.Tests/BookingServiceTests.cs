using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class BookingServiceTests
{
    [Fact]
    public async Task Create_on_free_slot_returns_active_with_policy_price()
    {
        using var app = new TestApp();
        var userId = await CreateUser(app, "booker@example.test");
        var courtId = await CreateCourt(app, "Booking court");
        var date = Next(DayOfWeek.Monday);
        await AddSchedule(app, courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0));

        using var scope = app.Services.CreateScope();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var booking = await bookings.CreateAsync(userId, courtId, date, new TimeOnly(9, 0));

        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal(BookingPolicy.PriceArs, booking.PriceArs);
        Assert.Equal(new TimeOnly(10, 0), booking.EndTime);
        Assert.Null(booking.CancelledAtUtc);
    }

    [Fact]
    public async Task Duplicate_create_returns_spanish_conflict()
    {
        using var app = new TestApp();
        var userId = await CreateUser(app, "dupe@example.test");
        var courtId = await CreateCourt(app, "Dupe court");
        var date = Next(DayOfWeek.Monday);
        await AddSchedule(app, courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0));

        using var scope = app.Services.CreateScope();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        await bookings.CreateAsync(userId, courtId, date, new TimeOnly(9, 0));
        var ex = await Assert.ThrowsAsync<BookingConflictException>(() =>
            bookings.CreateAsync(userId, courtId, date, new TimeOnly(9, 0)));
        Assert.Equal("Este horario ya fue reservado. Elegí otro horario disponible.", ex.Message);
    }

    [Fact]
    public async Task Availability_hides_active_booking_and_cancel_frees_it()
    {
        using var app = new TestApp();
        var userId = await CreateUser(app, "avail@example.test");
        var courtId = await CreateCourt(app, "Avail court");
        var date = Next(DayOfWeek.Monday);
        await AddSchedule(app, courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0));

        using (var scope = app.Services.CreateScope())
        {
            var availability = scope.ServiceProvider.GetRequiredService<CourtAvailabilityService>();
            Assert.Contains(new TimeOnly(9, 0),
                await availability.GetAvailableStartTimesAsync(courtId, date));
        }

        int bookingId;
        using (var scope = app.Services.CreateScope())
        {
            var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
            bookingId = (await bookings.CreateAsync(userId, courtId, date, new TimeOnly(9, 0))).Id;
        }

        using (var scope = app.Services.CreateScope())
        {
            var availability = scope.ServiceProvider.GetRequiredService<CourtAvailabilityService>();
            Assert.DoesNotContain(new TimeOnly(9, 0),
                await availability.GetAvailableStartTimesAsync(courtId, date));
        }

        using (var scope = app.Services.CreateScope())
        {
            var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
            var (cancelled, wasAlready) = await bookings.CancelAsync(bookingId, userId, isAdmin: false);
            Assert.False(wasAlready);
            Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
            Assert.NotNull(cancelled.CancelledAtUtc);
        }

        using (var scope = app.Services.CreateScope())
        {
            var availability = scope.ServiceProvider.GetRequiredService<CourtAvailabilityService>();
            Assert.Contains(new TimeOnly(9, 0),
                await availability.GetAvailableStartTimesAsync(courtId, date));
            var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
            var rebooked = await bookings.CreateAsync(userId, courtId, date, new TimeOnly(9, 0));
            Assert.Equal(BookingStatus.Active, rebooked.Status);
        }

        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Bookings WHERE Status = 0"));
    }

    [Theory]
    [InlineData("past")]
    [InlineData("inactive")]
    [InlineData("unknown")]
    [InlineData("scheduleless")]
    [InlineData("closure")]
    public async Task Create_rejects_invalid_slots_with_spanish_messages(string kind)
    {
        using var app = new TestApp();
        var userId = await CreateUser(app, $"invalid-{kind}@example.test");
        var courtId = await CreateCourt(app, $"Invalid {kind} court");
        var activeDate = Next(DayOfWeek.Monday);
        await AddSchedule(app, courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0));

        int targetCourt = courtId;
        DateOnly date = activeDate;
        TimeOnly start = new TimeOnly(9, 0);
        switch (kind)
        {
            case "past":
                date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
                break;
            case "inactive":
                await DeactivateCourt(app, courtId);
                break;
            case "unknown":
                targetCourt = 999_999;
                break;
            case "scheduleless":
                date = Next(DayOfWeek.Tuesday);
                break;
            case "closure":
                await AddClosure(app, courtId, activeDate, new TimeOnly(9, 0), new TimeOnly(10, 0));
                break;
        }

        using var scope = app.Services.CreateScope();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        if (kind == "unknown")
        {
            var ex = await Assert.ThrowsAsync<BookingNotFoundException>(() =>
                bookings.CreateAsync(userId, targetCourt, date, start));
            Assert.Equal("La cancha solicitada no existe.", ex.Message);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<BookingValidationException>(() =>
                bookings.CreateAsync(userId, targetCourt, date, start));
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            if (kind == "past") Assert.Equal("No se puede reservar un horario pasado. Elegí una fecha futura.", ex.Message);
            if (kind == "inactive") Assert.Equal("Esta cancha no está disponible para reservas.", ex.Message);
            if (kind == "closure") Assert.Equal("Este horario no está disponible por un bloqueo de la cancha.", ex.Message);
            if (kind == "scheduleless") Assert.Equal("Selecciona una fecha válida.", ex.Message);
        }
    }

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    private static async Task<string> CreateUser(TestApp app, string email)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        return user.Id;
    }

    private static async Task<int> CreateCourt(TestApp app, string name)
    {
        using var scope = app.Services.CreateScope();
        var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
        await courts.CreateAsync(new CourtForm { Name = name, Description = "Test court", IsActive = true });
        return (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == name).Id;
    }

    private static async Task DeactivateCourt(TestApp app, int courtId)
    {
        using var scope = app.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<CourtService>().DeactivateAsync(courtId));
    }

    private static async Task AddSchedule(TestApp app, int courtId, DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
            .CreateScheduleAsync(courtId, day, start, end);
    }

    private static async Task AddClosure(TestApp app, int courtId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
            .CreateClosureAsync(courtId, date, start, end);
    }
}
