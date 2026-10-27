using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// No endpoint in the Api forbids a caller yet, so this appends one path to the end of the real pipeline that calls
/// the cookie scheme's forbid, the way an authorisation policy would. It runs inside the Api's status-code pages.
/// </summary>
public static class ForbidProbe
{
    public const string Path = "/test/forbid";

    public static void Register(IServiceCollection services) => services.AddSingleton<IStartupFilter, Filter>();

    private sealed class Filter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == Path)
                {
                    await context.ForbidAsync();
                    return;
                }

                await nextMiddleware(context);
            });
        };
    }
}
