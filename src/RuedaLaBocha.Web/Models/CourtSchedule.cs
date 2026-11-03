namespace RuedaLaBocha.Web.Models;

public sealed class CourtSchedule
{
    public int Id { get; set; }

    public int CourtId { get; set; }

    public Court Court { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}
