namespace DisputeDesk.Api.Security;

/// <summary>The <c>ForwardedHeaders</c> configuration section: who may forward headers, and for which host (AD-23, AD-26).</summary>
public sealed class ForwardedHeadersTrustOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>The network in CIDR notation whose connections may forward headers: Compose's default network locally.</summary>
    public string TrustedNetwork { get; set; } = "";

    /// <summary>The public app host, without a port: the one <c>X-Forwarded-Host</c> value taken (<c>localhost</c> locally).</summary>
    public string AllowedHosts { get; set; } = "";
}
