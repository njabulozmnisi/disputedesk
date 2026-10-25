namespace DisputeDesk.Api.Security;

/// <summary>
/// Response headers the Api owns. CSP and the rest of the browser policy live on the nginx gateway. The Server
/// header is switched off on Kestrel itself (see Program).
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
                var headers = ((HttpResponse)state).Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                return Task.CompletedTask;
            }, context.Response);
            return next(context);
        });
}
