namespace RuedaLaBocha.Web.Services;

public sealed class BookingNotFoundException(string message) : Exception(message);

public sealed class BookingForbiddenException(string message) : Exception(message);

public sealed class BookingValidationException(string message) : Exception(message);

public sealed class BookingConflictException(string message) : Exception(message)
{
    public const string SlotTakenMessage = "Este horario ya fue reservado. Elegí otro horario disponible.";
}
