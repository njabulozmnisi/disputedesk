using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Disputes.Idempotency;

/// <summary>
/// Makes the raise idempotent under its <c>Idempotency-Key</c> (AD-16, draft-ietf-httpapi-idempotency-key-header), as an
/// endpoint filter that runs after authorisation and built-in body validation. It claims the key under a fresh lease;
/// the handler commits the 201 with the dispute; a 4xx is stored and any 5xx releases the key, both only while the
/// lease is still this request's. A completed key replays its stored response; a different request under the key
/// gets 422 <c>idempotency-mismatch</c>, and a key another request holds gets 409 <c>idempotency-in-progress</c>.
/// </summary>
public sealed class IdempotencyFilter(
    IAppDbContext db,
    CurrentCustomer currentCustomer,
    ClaimedIdempotencyKey claimed,
    TimeProvider time,
    IOptions<JsonOptions> jsonOptions) : IEndpointFilter
{
    private static readonly Error InProgress = Error.Conflict(
        DisputeProblems.IdempotencyInProgress, "A request with this Idempotency-Key is still being processed.");

    private static readonly Error Mismatch = Error.BusinessRule(
        DisputeProblems.IdempotencyMismatch, "This Idempotency-Key was already used with a different request.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        string? key = IdempotencyKeyHeader.Parse(http.Request.Headers[IdempotencyKeyHeader.Name]);
        if (key is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "The Idempotency-Key header must be one quoted string of 1 to 255 printable characters, such as \"<uuid>\".");
        }

        // An empty or null body is a required parameter that failed to bind: the framework has set 400 and still runs
        // the filters, then skips the handler. There is no request to claim the key for.
        if (http.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return await next(context);
        }

        byte[] requestHash = HashOf(context.GetArgument<RaiseDisputeRequest>(0));
        IdempotencyKey? ours = await db.Entities<IdempotencyKey>().ClaimAsync(
            currentCustomer.CustomerId, key, requestHash, time.GetUtcNow(), http.RequestAborted);
        if (ours is null)
        {
            return await AnswerForExistingKeyAsync(key, requestHash, http);
        }

        claimed.Set(ours);
        try
        {
            object? result = await next(context);
            return await FinishAsync(result, ours, http);
        }
        catch
        {
            // A 500, a 504 from the request timeout, or an unexpected result: the request failed, so the key is
            // released for a fresh retry.
            await ReleaseAsync(ours);
            throw;
        }
    }

    /// <summary>
    /// The SHA-256 of the validated request re-serialised with the API's JSON settings: properties in their declared
    /// order, unknown properties already dropped by binding, and values (amounts included) exactly as sent.
    /// </summary>
    private byte[] HashOf(RaiseDisputeRequest request) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, jsonOptions.Value.SerializerOptions));

    private async Task<object?> AnswerForExistingKeyAsync(string key, byte[] requestHash, HttpContext http)
    {
        IdempotencyKey? existing = await db.Entities<IdempotencyKey>().AsNoTracking().SingleOrDefaultAsync(
            row => row.CustomerId == currentCustomer.CustomerId && row.Key == key, http.RequestAborted);

        // Released by its holder between the claim and this read: the client's retry will claim it.
        if (existing is null)
        {
            return InProgress.ToProblem();
        }

        if (!existing.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return Mismatch.ToProblem();
        }

        if (existing.Status != IdempotencyKeyStatus.Completed)
        {
            return InProgress.ToProblem();
        }

        return StoredResponse.From(existing, http.Response);
    }

    /// <summary>
    /// The 201 was committed with the dispute and comes back as its stored copy. A 4xx from the handler is stored now,
    /// and a 5xx releases the key. Any other result is a programming error and throws, which releases the key.
    /// </summary>
    private async Task<object?> FinishAsync(object? result, IdempotencyKey ours, HttpContext http)
    {
        if (result is ContentHttpResult { StatusCode: StatusCodes.Status201Created })
        {
            return result;
        }

        if (result is not ProblemHttpResult problem)
        {
            throw new InvalidOperationException($"The raise returned an unexpected result: {result?.GetType().Name ?? "null"}.");
        }

        if (problem.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            await ReleaseAsync(ours);
            return problem;
        }

        string body = await RenderAsync(problem, http);
        await StoreAsync(ours, problem.StatusCode, body);
        return StoredResponse.Create(problem.StatusCode, body, location: null, http.Response);
    }

    /// <summary>
    /// Writes the problem as the Api writes every problem (its type and <c>traceId</c> added by the ProblemDetails
    /// service) into memory instead of the response, so the stored copy is exactly what the client then receives.
    /// </summary>
    private static async Task<string> RenderAsync(ProblemHttpResult problem, HttpContext http)
    {
        Stream responseBody = http.Response.Body;
        using var rendered = new MemoryStream();
        http.Response.Body = rendered;
        try
        {
            await problem.ExecuteAsync(http);
        }
        finally
        {
            http.Response.Body = responseBody;
        }

        return Encoding.UTF8.GetString(rendered.ToArray());
    }

    // Both writes are conditional on the lease and on the key still being in progress, so a request that lost its
    // lease to a take-over never overwrites the new holder's row, and a completed key is never released. They run
    // with CancellationToken.None so a timed-out or aborted request still finishes them.
    private Task<int> StoreAsync(IdempotencyKey ours, int status, string body) =>
        HeldByUs(ours).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.Status, IdempotencyKeyStatus.Completed)
                .SetProperty(row => row.ResponseStatus, status)
                .SetProperty(row => row.ResponseBody, body),
            CancellationToken.None);

    private Task<int> ReleaseAsync(IdempotencyKey ours) => HeldByUs(ours).ExecuteDeleteAsync(CancellationToken.None);

    private IQueryable<IdempotencyKey> HeldByUs(IdempotencyKey ours) =>
        db.Entities<IdempotencyKey>().Where(row =>
            row.Id == ours.Id && row.LeaseId == ours.LeaseId && row.Status == IdempotencyKeyStatus.InProgress);
}
