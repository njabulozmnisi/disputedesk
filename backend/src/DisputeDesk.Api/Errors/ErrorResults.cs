using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DisputeDesk.Api.Errors;

/// <summary>
/// Turns an expected failure into a ProblemDetails response with the status its kind implies. The only place an
/// <see cref="Error"/> becomes HTTP (AD-3). Malformed input never gets here: built-in minimal API validation answers 400.
/// </summary>
public static class ErrorResults
{
    public static ProblemHttpResult ToProblem(this Error error)
    {
        ProblemDetails problem = error.FieldErrors.Count > 0
            ? new HttpValidationProblemDetails(error.FieldErrors.ToDictionary())
            : new ProblemDetails();
        problem.Status = StatusCodeFor(error.Kind);
        problem.Type = ProblemType.For(error.Code);
        problem.Detail = error.Message;
        foreach ((string name, object? value) in error.Extensions)
        {
            problem.Extensions[name] = value;
        }

        return TypedResults.Problem(problem);
    }

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
