namespace RuedaLaBocha.Web.Models;

public sealed record BookingSummaryViewModel(
    Court Court,
    DateOnly Date,
    IReadOnlyList<TimeOnly> Starts,
    IReadOnlyList<TimeOnly> Ends,
    int Hours,
    decimal Total,
    bool IsPromo);
