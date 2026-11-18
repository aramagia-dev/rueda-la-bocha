namespace RuedaLaBocha.Web.Models;

public enum SlotState
{
    Free,
    Taken,
    Blocked
}

public sealed record SlotStatus(TimeOnly Start, TimeOnly End, SlotState State);
