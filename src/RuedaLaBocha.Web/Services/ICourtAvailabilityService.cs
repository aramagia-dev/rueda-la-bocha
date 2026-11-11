namespace RuedaLaBocha.Web.Services;

// Seam de testabilidad para el estado de error de /Reservas (QA320): el controlador
// depende de esta abstracción y el falso de integración la rompe sin tocar la DB real.
public interface ICourtAvailabilityService
{
    Task<List<TimeOnly>> GetAvailableStartTimesAsync(int courtId, DateOnly date);
}
