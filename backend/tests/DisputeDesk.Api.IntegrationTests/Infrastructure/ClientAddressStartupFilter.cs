using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Sets <c>Connection.RemoteIpAddress</c> on every request before the rest of the pipeline runs, as Kestrel would from
/// the socket (Microsoft Learn: "Use IStartupFilter to configure middleware at the start of the pipeline"). A filter
/// registered later runs after the earlier one, so a test can register its own address over <see cref="ApiFactory"/>'s.
/// </summary>
public sealed class ClientAddressStartupFilter(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = address;
            return nextMiddleware(context);
        });
        next(app);
    };
}
