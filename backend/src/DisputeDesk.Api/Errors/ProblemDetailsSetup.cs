using System.Diagnostics;
using System.Text.Json;
using DisputeDesk.Contracts.Problems;
using Microsoft.AspNetCore.Mvc;

namespace DisputeDesk.Api.Errors;

/// <summary>
/// The last step for every problem body the Api writes (AD-3), whoever produced it: <see cref="ErrorResults"/>, the
/// validation filter, the auth handlers, the antiforgery filter, the exception handler or the status-code pages.
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddDisputeDeskProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context => Customize(context.ProblemDetails, context.HttpContext));

    private static void Customize(ProblemDetails problem, HttpContext http)
    {
        // traceId is the W3C trace ID of the request (32 hex characters), the value to search for in the trace viewer.
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToHexString() ?? http.TraceIdentifier;

        // ErrorResults sets the type from the error's slug; framework responses get the slug their status implies.
        if (problem.Type is null || !problem.Type.StartsWith(ProblemType.Prefix, StringComparison.Ordinal))
        {
            problem.Type = ProblemType.For(SlugFor(problem.Status ?? http.Response.StatusCode));
        }

        if (problem is HttpValidationProblemDetails validation)
        {
            validation.Errors = validation.Errors.ToDictionary(entry => CamelCase(entry.Key), entry => entry.Value);
        }
    }

    /// <summary>
    /// The slug for a response that carries no slug of its own. Other 4xx statuses (405, 415 and the like) mean the
    /// request itself is malformed, so they are <c>validation</c>.
    /// </summary>
    private static string SlugFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => CommonProblems.Unauthorized,
        StatusCodes.Status403Forbidden => CommonProblems.Forbidden,
        StatusCodes.Status404NotFound => CommonProblems.NotFound,
        StatusCodes.Status429TooManyRequests => CommonProblems.RateLimited,
        < StatusCodes.Status500InternalServerError => CommonProblems.Validation,
        _ => CommonProblems.Internal,
    };

    /// <summary><c>Note</c> becomes <c>note</c>, and <c>Items[0].Name</c> becomes <c>items[0].name</c>.</summary>
    private static string CamelCase(string key) =>
        string.Join('.', key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
