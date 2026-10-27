using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Authentication;

/// <summary>
/// The <c>Authentication</c> section (AD-24, AD-26): cookie names and the Secure policy, set per environment in configuration.
/// appsettings.json holds the AWS values (<c>__Host-</c> names, Always); local Compose overrides them.
/// </summary>
public sealed class BrowserCookieOptions
{
    public const string Section = "Authentication";

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
