using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Api.Bff;

public sealed class CookieNameOptions
{
    [Required]
    public string Name { get; set; } = "";
}
