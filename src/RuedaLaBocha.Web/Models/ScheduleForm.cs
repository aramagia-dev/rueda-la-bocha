using System.ComponentModel.DataAnnotations;

namespace RuedaLaBocha.Web.Models;

public sealed class ScheduleForm : IValidatableObject
{
    [Required(ErrorMessage = "Selecciona un día."), EnumDataType(typeof(DayOfWeek), ErrorMessage = "Selecciona un día válido.")]
    [Display(Name = "Día de la semana")]
    public DayOfWeek? DayOfWeek { get; set; }

    [Required(ErrorMessage = "Ingresa la hora de apertura."), Display(Name = "Apertura")]
    public TimeOnly? StartTime { get; set; }

    [Required(ErrorMessage = "Ingresa la hora de cierre."), Display(Name = "Cierre")]
    public TimeOnly? EndTime { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartTime.HasValue && EndTime.HasValue && StartTime >= EndTime)
            yield return new ValidationResult("El cierre debe ser posterior a la apertura, dentro del mismo día.", [nameof(EndTime)]);
    }
}
