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

    /// <summary>The OpenID Connect handler's correlation cookie; the handler appends the correlation ID to the name.</summary>
    [Required]
    [ValidateObjectMembers]
    public CookieNameOptions CorrelationCookie { get; set; } = new();

    /// <summary>The OpenID Connect handler's nonce cookie; the handler appends the protected nonce to the name.</summary>
    [Required]
    [ValidateObjectMembers]
    public CookieNameOptions NonceCookie { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public CookieSecurityOptions Cookie { get; set; } = new();
}
