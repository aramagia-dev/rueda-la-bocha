namespace RuedaLaBocha.Web.Models;

public static class BookingPolicy
{
    public const string Sport = "Football 5";
    public const int DurationMinutes = 60;
    public const decimal PriceArs = 40000m;
    public static string PriceDisplay => $"ARS {PriceArs.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-AR"))}";
}
