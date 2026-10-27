using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Api.Authentication;

public sealed class CookieNameOptions
{
    [Required]
    public string Name { get; set; } = "";
}
