using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Appends one path under <c>/api</c> to the end of the real pipeline that throws, so a test can drive the
/// unhandled-exception path for an API response. It runs inside the Api's exception handler.
/// </summary>
public static class ThrowProbe
{
    public const string Path = "/api/test/throw";

    public static void Register(IServiceCollection services) => services.AddSingleton<IStartupFilter, Filter>();

    private sealed class Filter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Path == Path)
                {
                    throw new InvalidOperationException("Thrown by the test probe.");
                }

                return nextMiddleware(context);
            });
        };
    }
}
