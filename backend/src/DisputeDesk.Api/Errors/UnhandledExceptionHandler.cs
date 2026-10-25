using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DisputeDesk.Api.Errors;

/// <summary>
/// The last line for unexpected failures: logs the exception and returns a 500 ProblemDetails that
/// carries the traceId and nothing else. No message, no stack trace, in every environment.
/// </summary>
public sealed partial class UnhandledExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.GetEndpoint()?.DisplayName);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Method} {Endpoint}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string? endpoint);
}
