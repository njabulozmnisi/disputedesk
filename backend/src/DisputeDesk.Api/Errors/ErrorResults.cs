using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputeDesk.Api.Errors;

/// <summary>Turns an expected failure into a ProblemDetails response with the status its kind implies.</summary>
public static class ErrorResults
{
    public static ProblemHttpResult ToProblem(this Error error) => TypedResults.Problem(
        statusCode: StatusCodeFor(error.Kind),
        detail: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static int StatusCodeFor(ErrorKind kind) => kind switch
    {
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.InvalidTransition => StatusCodes.Status409Conflict,
        ErrorKind.Validation or ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped error kind."),
    };
}
