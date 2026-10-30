using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Security;

/// <summary>
/// AD-23: the Forwarded Headers middleware takes the client address, scheme and host from the gateway's
/// <c>X-Forwarded-*</c> headers, but only on a connection from <c>ForwardedHeaders:TrustedNetwork</c>, and the host only
/// when it is <c>ForwardedHeaders:AllowedHosts</c> (Microsoft Learn, "Configure ASP.NET Core to work with proxy servers
/// and load balancers"). Every other connection keeps its own address, scheme and host.
/// </summary>
public static class ForwardedHeadersTrust
{
    public static IServiceCollection AddForwardedHeadersTrust(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersTrustOptions>()
            .BindConfiguration(ForwardedHeadersTrustOptions.SectionName)
            .Validate(
                options => System.Net.IPNetwork.TryParse(options.TrustedNetwork, out _),
                "ForwardedHeaders:TrustedNetwork must be a network in CIDR notation, such as 10.201.89.0/24.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AllowedHosts),
                "ForwardedHeaders:AllowedHosts must hold the public app host, without a port.")
            // The middleware compares the host without its port, and takes * as any host and *.x as any subdomain of x.
            .Validate(
                options => !options.AllowedHosts.Contains(':', StringComparison.Ordinal) && !options.AllowedHosts.Contains('*', StringComparison.Ordinal),
                "ForwardedHeaders:AllowedHosts must be one host name, without a port or a wildcard.")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersTrustOptions>>((options, trust) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                // The defaults trust loopback (::1 and 127.0.0.0/8); only the configured network is trusted instead.
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(trust.Value.TrustedNetwork));
                // The gateway sends one value and replaces any the client sent, so only the last entry is its own.
                options.ForwardLimit = 1;
                options.AllowedHosts = [trust.Value.AllowedHosts];
            });

        return services;
    }
}
