using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Tests;

public sealed class BookingTests
{
    [Fact]
    public void Booking_defaults_follow_policy_invariants()
    {
        Assert.Equal(0, (int)BookingStatus.Active);
        Assert.Equal(1, (int)BookingStatus.Cancelled);
        var booking = new Booking
        {
            UserId = "user-1",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(10, 0).Add(TimeSpan.FromMinutes(BookingPolicy.DurationMinutes)),
            PriceArs = BookingPolicy.PriceArs,
            Status = BookingStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
        };
        Assert.Equal(TimeSpan.FromMinutes(BookingPolicy.DurationMinutes), booking.EndTime - booking.StartTime);
        Assert.Equal(40_000m, booking.PriceArs);
        Assert.Null(booking.CancelledAtUtc);
    }
}
