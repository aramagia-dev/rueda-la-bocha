namespace RuedaLaBocha.Web.Models;

public sealed record CourtHoursViewModel(Court Court, IReadOnlyList<CourtSchedule> Schedules, IReadOnlyList<CourtClosure> Closures);

public sealed record CourtAvailabilityViewModel(Court Court, DateOnly Date, IReadOnlyList<TimeOnly> StartTimes);
