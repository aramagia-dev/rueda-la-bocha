using System.ComponentModel.DataAnnotations;

namespace RuedaLaBocha.Web.Models;

public class CourtForm
{
    [Display(Name = "Nombre")]
    [Required(ErrorMessage = "Ingresa el nombre de la cancha.")]
    [StringLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres.")]
    public string Name { get; set; } = "";

    [Display(Name = "Descripción")]
    [StringLength(1000, ErrorMessage = "La descripción no puede superar los 1000 caracteres.")]
    public string? Description { get; set; }

    [Display(Name = "Cancha activa")]
    public bool IsActive { get; set; } = true;
}
