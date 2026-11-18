using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Services;

public interface IPromoService
{
    bool IsPromoDay(DayOfWeek day);

    Task<decimal> GetTotalPriceAsync(DateOnly date, int slotCount);
}

public sealed class PromoService(ApplicationDbContext db) : IPromoService
{
    public const int PromoHours = 3;
    public const decimal PromoTotalPrice = 45_000m;

    public static bool IsPromoDayOfWeek(DayOfWeek day) =>
        day is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday;

    public bool IsPromoDay(DayOfWeek day) => IsPromoDayOfWeek(day);

    public async Task<decimal> GetTotalPriceAsync(DateOnly date, int slotCount)
    {
        if (slotCount == PromoHours && IsPromoDayOfWeek(date.DayOfWeek))
        {
            var promo = await db.Promos.AsNoTracking()
                .FirstOrDefaultAsync(p => p.MinHours == slotCount &&
                    p.StartDay <= date.DayOfWeek && date.DayOfWeek <= p.EndDay);
            if (promo is not null) return promo.TotalPrice;
            return PromoTotalPrice;
        }

        return BookingPolicy.PriceArs * slotCount;
    }
}
