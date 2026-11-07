using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class BookingConcurrencyTests
{
    [Fact]
    public async Task Parallel_creates_leave_single_active_and_spanish_conflicts()
    {
        using var app = new TestApp();
        string userId;
        int courtId;
        var date = Next(DayOfWeek.Monday);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = "race@example.test", Email = "race@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
            userId = user.Id;
            var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
            await courts.CreateAsync(new CourtForm { Name = "Race court", Description = "t", IsActive = true });
            courtId = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == "Race court").Id;
            await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
                .CreateScheduleAsync(courtId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0));
        }

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
            try
            {
                await bookings.CreateAsync(userId, courtId, date, new TimeOnly(9, 0));
                return "created";
            }
            catch (BookingConflictException ex)
            {
                Assert.Equal("Este horario ya fue reservado. Elegí otro horario disponible.", ex.Message);
                return "conflict";
            }
        })));

        Assert.Equal(1, attempts.Count(a => a == "created"));
        Assert.Equal(7, attempts.Count(a => a == "conflict"));
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Bookings WHERE Status = 0"));
    }

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }
}
