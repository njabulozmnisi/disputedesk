using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputeDesk.Api.Errors;

/// <summary>
/// Turns an expected failure into a ProblemDetails response with the status its kind implies. The only place an
/// <see cref="Error"/> becomes HTTP (AD-3). Malformed input never gets here: built-in minimal API validation answers 400.
/// </summary>
public static class ErrorResults
{
    public static ProblemHttpResult ToProblem(this Error error) => TypedResults.Problem(
        statusCode: StatusCodeFor(error.Kind),
        detail: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static int StatusCodeFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.ActionNotAllowed or ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped error kind."),
    };
}
