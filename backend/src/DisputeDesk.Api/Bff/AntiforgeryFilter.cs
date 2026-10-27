using Microsoft.AspNetCore.Antiforgery;

namespace DisputeDesk.Api.Bff;

/// <summary>
/// Requires the antiforgery token (the <c>X-XSRF-TOKEN</c> header, or the form field on the logout form) on every
/// unsafe request from a signed-in caller (AD-7). The built-in middleware checks only form-bound endpoints and never
/// DELETE, so cookie-authenticated JSON endpoints run this filter instead. An anonymous caller has no session for a
/// forged request to ride on, so it is not checked. A failure is a 400 <c>validation</c> problem.
/// </summary>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        bool isSafeMethod = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method) || HttpMethods.IsTrace(http.Request.Method);
        bool isSignedIn = http.User.Identity?.IsAuthenticated == true;
        if (isSafeMethod || !isSignedIn)
        {
            return await next(context);
        }

        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The antiforgery token is missing or invalid.");
        }

        return await next(context);
    }
}
