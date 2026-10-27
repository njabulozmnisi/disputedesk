using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Bff;

/// <summary>The <c>Zitadel</c> section (AD-26). Locally the provisioner writes it to zitadel-clients.json.</summary>
public sealed class ZitadelOptions
{
    public const string Section = "Zitadel";

    [Required]
    public string Issuer { get; set; } = "";

    /// <summary>Where to read the discovery document, when it differs from the issuer's well-known address.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Only the local http stack turns this off (docker-compose.yml).</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    [Required]
    [ValidateObjectMembers]
    public ZitadelClientOptions Bff { get; set; } = new();
}
