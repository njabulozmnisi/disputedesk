using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Api.Bff;

/// <summary>The BFF's confidential client: <c>Zitadel:Bff:ClientId</c> and <c>Zitadel:Bff:ClientSecret</c>.</summary>
public sealed class ZitadelClientOptions
{
    [Required]
    public string ClientId { get; set; } = "";

    [Required]
    public string ClientSecret { get; set; } = "";
}
