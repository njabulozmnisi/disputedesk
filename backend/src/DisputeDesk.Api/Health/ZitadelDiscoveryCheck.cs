using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Health;

/// <summary>
/// Ready only when the sign-in handler can load Zitadel's discovery document (AD-25). It asks the handler's own
/// configuration manager, so it tests exactly what sign-in uses, cache included.
/// </summary>
public sealed class ZitadelDiscoveryCheck(IOptionsMonitor<OpenIdConnectOptions> oidcOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        OpenIdConnectOptions oidc = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        if (oidc.ConfigurationManager is null)
        {
            return HealthCheckResult.Unhealthy("The OpenID Connect handler has no configuration manager.");
        }

        try
        {
            await oidc.ConfigurationManager.GetConfigurationAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Zitadel discovery failed.", exception);
        }
    }
}
