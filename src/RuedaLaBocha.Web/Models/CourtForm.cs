using System.ComponentModel.DataAnnotations;

namespace RuedaLaBocha.Web.Models;

public class CourtForm
{
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
