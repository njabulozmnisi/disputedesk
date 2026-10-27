using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Bff;

/// <summary>
/// The <c>Bff</c> section (AD-24, AD-26): cookie names and the Secure policy, set per environment in configuration.
/// appsettings.json holds the AWS values (<c>__Host-</c> names, Always); local Compose overrides them.
/// </summary>
public sealed class BffOptions
{
    public const string Section = "Bff";

    [Required]
    [ValidateObjectMembers]
    public CookieNameOptions SessionCookie { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public CookieNameOptions AntiforgeryCookie { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public CookieSecurityOptions Cookie { get; set; } = new();
}
