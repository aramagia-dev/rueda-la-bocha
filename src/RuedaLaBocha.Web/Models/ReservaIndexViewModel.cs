namespace RuedaLaBocha.Web.Models;

public sealed record ReservaCourtSlots(Court Court, IReadOnlyList<TimeOnly> Slots);

public sealed record ReservaIndexViewModel(
    DateOnly Today,
    DateOnly? Selected,
    IReadOnlyList<ReservaCourtSlots> Courts,
    string? Message);
