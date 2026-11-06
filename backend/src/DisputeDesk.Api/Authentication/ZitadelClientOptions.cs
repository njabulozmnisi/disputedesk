using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Api.Authentication;

/// <summary>The web app's confidential client in Zitadel: <c>Zitadel:WebApp:ClientId</c> and <c>Zitadel:WebApp:ClientSecret</c>.</summary>
public sealed class ZitadelClientOptions
{
    [Required]
    public string ClientId { get; set; } = "";

    [Required]
    public string ClientSecret { get; set; } = "";
}
