namespace DisputeDesk.Api.Security;

/// <summary>
/// The one response header the Api owns: <c>Cache-Control: no-store</c> on everything under <c>/api</c>. The nginx
/// gateway owns the browser security headers (CSP, X-Content-Type-Options, Referrer-Policy and the rest) and sets each
/// once, so the Api does not repeat them (AD-23). The Server header is switched off on Kestrel itself (see Program).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Sets the header just before the response starts, so a response rewritten later in the pipeline keeps
    /// it: the exception handler clears all headers before it writes the 500.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
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
