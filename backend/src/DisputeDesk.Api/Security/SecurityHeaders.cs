namespace DisputeDesk.Api.Security;

/// <summary>
/// Response headers the Api owns, so they hold with or without the gateway: nosniff, no referrer, and no-store on
/// everything under <c>/api</c>. CSP and the rest of the browser policy live on the nginx gateway. The Server header
/// is switched off on Kestrel itself (see Program).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Sets the headers just before the response starts, so a response rewritten later in the pipeline keeps
    /// them: the exception handler clears all headers before it writes the 500.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                response.Headers.XContentTypeOptions = "nosniff";
                response.Headers["Referrer-Policy"] = "no-referrer";
                // API responses carry customer data and session state, so no browser or proxy may keep a copy.
                if (response.HttpContext.Request.Path.StartsWithSegments("/api"))
                {
                    response.Headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            }, context.Response);
            return next(context);
        });
}
