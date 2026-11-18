namespace RuedaLaBocha.Web.Models;

public sealed class Promo
{
    public int Id { get; set; }

    public string Name { get; set; } = "Weekday 3h";

    public DayOfWeek StartDay { get; set; } = DayOfWeek.Monday;

    public DayOfWeek EndDay { get; set; } = DayOfWeek.Thursday;

    public int MinHours { get; set; } = 3;

    public decimal TotalPrice { get; set; } = 45_000m;
}
