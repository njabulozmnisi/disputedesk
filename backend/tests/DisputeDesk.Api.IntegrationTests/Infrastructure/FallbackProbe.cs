using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Maps one routed endpoint that carries no authorization metadata, onto the Api's own route builder, so the
/// fallback policy alone decides who reaches it. Only the factory that registers it has it.
/// </summary>
public static class FallbackProbe
{
    public const string Path = "/api/test/no-policy";

    public static void Register(IServiceCollection services) => services.AddSingleton<IStartupFilter, Filter>();

    private sealed class Filter : IStartupFilter
    {
        // UseEndpoints maps onto the route builder the Api's UseRouting already matches against, so the authorization
        // middleware sees this endpoint like any other.
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(endpoints => endpoints.MapGet(Path, () => TypedResults.Ok()));
        };
    }
}
