using System.ComponentModel.DataAnnotations;

namespace RuedaLaBocha.Web.Models;

public class Court
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(1000)]
    public string Description { get; set; } = "";

    public bool IsActive { get; set; } = true;
}
