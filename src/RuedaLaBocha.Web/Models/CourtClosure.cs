namespace RuedaLaBocha.Web.Models;

public sealed class CourtClosure
{
    public int Id { get; set; }

    public int CourtId { get; set; }

    public Court Court { get; set; } = null!;

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}
