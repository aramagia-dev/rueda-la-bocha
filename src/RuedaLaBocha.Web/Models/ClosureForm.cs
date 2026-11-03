using System.ComponentModel.DataAnnotations;

namespace RuedaLaBocha.Web.Models;

public sealed class ClosureForm : IValidatableObject
{
    [Required(ErrorMessage = "Selecciona una fecha."), Display(Name = "Fecha")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Ingresa la hora de inicio."), Display(Name = "Desde")]
    public TimeOnly? StartTime { get; set; }

    [Required(ErrorMessage = "Ingresa la hora de finalización."), Display(Name = "Hasta")]
    public TimeOnly? EndTime { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartTime.HasValue && EndTime.HasValue && StartTime >= EndTime)
            yield return new ValidationResult("La finalización debe ser posterior al inicio, dentro del mismo día.", [nameof(EndTime)]);
    }
}
