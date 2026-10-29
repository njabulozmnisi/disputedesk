using DisputeDesk.Application.Disputes.RaiseDispute;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputeDesk.Api.Disputes.Idempotency;

/// <summary>
/// A response stored under an idempotency key, written exactly as stored with the built-in content result. Every
/// response under a key, the first one and each replay alike, is written from the stored copy, so a replay is the
/// original byte for byte: status, <c>Location</c> and body, the original <c>traceId</c> included.
/// </summary>
public static class StoredResponse
{
    /// <summary>The response stored on a completed key.</summary>
    public static ContentHttpResult From(IdempotencyKey key, HttpResponse response) => Create(
        key.ResponseStatus ?? throw new InvalidOperationException("The idempotency key has no stored response."),
        key.ResponseBody ?? throw new InvalidOperationException("The idempotency key has no stored response."),
        key.ResponseLocation,
        response);

    /// <summary>Sets <c>Location</c> on <paramref name="response"/> when there is one, and returns the stored body as it was.</summary>
    public static ContentHttpResult Create(int status, string body, string? location, HttpResponse response)
    {
        if (location is not null)
        {
            response.Headers.Location = location;
        }

        // Only a 201 and the handler's 4xx problems are stored, so the status says which of the two media types it was.
        // No content encoding is passed, so the content type is written exactly as given.
        string contentType = status < StatusCodes.Status400BadRequest ? "application/json; charset=utf-8" : "application/problem+json";
        return TypedResults.Content(body, contentType, statusCode: status);
    }
}
