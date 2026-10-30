using System.ComponentModel.DataAnnotations;
using DisputeDesk.Api.Disputes.Idempotency;
using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Disputes.GetDispute;
using DisputeDesk.Application.Disputes.ListDisputes;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Application.Disputes.WithdrawDispute;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputeDesk.Api.Disputes;

/// <summary>
/// <c>POST /api/v1/disputes</c>, <c>GET /api/v1/disputes</c>, <c>GET /api/v1/disputes/{disputeId}</c> and
/// <c>POST /api/v1/disputes/{disputeId}/withdraw</c>, on the customer routes.
/// </summary>
public static class DisputeEndpoints
{
    /// <summary>
    /// The raise's request timeout policy, 30 seconds (AD-16): shorter than the 60 seconds after which another request
    /// may take an idempotency key over, so a live raise is never taken over. A named policy so tests can shorten it.
    /// </summary>
    public const string RaiseTimeoutPolicy = "raise";

    public static IServiceCollection AddDisputes(this IServiceCollection services) =>
        services.AddScoped<RaiseDisputeHandler>()
            .AddScoped<GetDisputeHandler>()
            .AddScoped<ListDisputesHandler>()
            .AddScoped<WithdrawDisputeHandler>()
            .AddScoped<ClaimedIdempotencyKey>()
            .AddSingleton<RaiseMetrics>()
            .AddSingleton<DisputeActionMetrics>()
            .Configure<RequestTimeoutOptions>(options => options.AddPolicy(RaiseTimeoutPolicy, TimeSpan.FromSeconds(30)));

    public static RouteGroupBuilder MapDisputes(this RouteGroupBuilder customerRoutes)
    {
        customerRoutes.MapPost("/disputes", RaiseAsync)
            .WithTags("Disputes")
            .Produces<DisputeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithRequestTimeout(RaiseTimeoutPolicy)
            .AddOpenApiOperationTransformer(DocumentIdempotencyKeyAsync);
        customerRoutes.MapGet(DisputeRoutes.List, ListAsync)
            .WithTags("Disputes")
            .ProducesValidationProblem();
        customerRoutes.MapGet(DisputeRoutes.Detail, GetAsync)
            .WithTags("Disputes")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        customerRoutes.MapPost(DisputeRoutes.Withdraw, WithdrawAsync)
            .WithTags("Disputes")
            // The note is checked by the aggregate after the dispute is loaded, so 404 comes before 400 (AD-8).
            .DisableValidation()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return customerRoutes;
    }

    /// <summary>
    /// The linked customer's disputes, newest first by when they were raised. <paramref name="limit"/> is 1 to 50 and
    /// defaults to 20; pass a page's <c>nextCursor</c> back as <paramref name="cursor"/> for the next page. 400
    /// <c>validation</c> naming the field for a limit out of range or not a whole number, and for a cursor longer than
    /// 128 characters or not one a page returned. It reads only what the raises stored, so it answers while core
    /// banking is down.
    /// </summary>
    /// <remarks>
    /// <c>[Range]</c> and <c>[MaxLength]</c> are checked by built-in validation; a limit that is not a whole number
    /// fails to bind and gets the same 400. The <c>Linked</c> policy runs in the authorization middleware, before
    /// binding and validation, so the checks run 401, 403, then 400 (AD-8).
    /// </remarks>
    private static async Task<Results<Ok<DisputePageResponse>, ProblemHttpResult>> ListAsync(
        ListDisputesHandler handler,
        [MaxLength(DisputeCursor.MaxLength)] string? cursor,
        [Range(1, ListDisputesHandler.MaxPageSize)] int limit = ListDisputesHandler.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<DisputePageResponse> result = await handler.HandleAsync(cursor, limit, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>
    /// Withdraws one of the linked customer's open disputes. 200 with the dispute as the detail shows it, its last
    /// timeline item the withdraw. 404 <c>not-found</c> for an unknown dispute and for another customer's alike; 400
    /// <c>validation</c> for a note over 500 characters after trimming; 409 <c>action-not-allowed</c> with
    /// <c>customerStatus</c> for a resolved or withdrawn dispute. A body or <paramref name="disputeId"/> that does not
    /// bind gets 400 before the lookup.
    /// </summary>
    /// <remarks>
    /// The checks run 401, 403, 404, 400, 409 (AD-8). Not idempotency-keyed: a repeat gets 409 with
    /// <c>customerStatus</c> <c>withdrawn</c>, which a client retrying may treat as success (FR-18).
    /// </remarks>
    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> WithdrawAsync(
        Guid disputeId,
        WithdrawDisputeRequest request,
        WithdrawDisputeHandler handler,
        DisputeActionMetrics metrics,
        CancellationToken cancellationToken)
    {
        Result<DisputeResponse> result = await handler.HandleAsync(disputeId, request, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        metrics.RecordWithdrawn(result.Value);
        return TypedResults.Ok(result.Value);
    }

    /// <summary>
    /// One of the linked customer's disputes, with its timeline oldest first. 404 <c>not-found</c> for an unknown
    /// dispute and for another customer's alike; 400 <c>validation</c> for a <paramref name="disputeId"/> that is not a
    /// GUID. It reads only what the raise stored, so it answers while core banking is down.
    /// </summary>
    /// <remarks>
    /// The <c>Linked</c> policy runs in the authorization middleware, before binding, so the checks run 401, 403, then
    /// 400 (AD-8). <paramref name="disputeId"/> has no route constraint on purpose: a value that is not a GUID then
    /// fails to bind and gets 400, where a <c>:guid</c> constraint would make it 404.
    /// </remarks>
    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> GetAsync(
        Guid disputeId,
        GetDisputeHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DisputeResponse> result = await handler.HandleAsync(disputeId, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>
    /// Raises a dispute on one of the linked customer's transactions. 201 with the dispute and its <c>Location</c>;
    /// 400 <c>validation</c> for a missing or malformed <c>Idempotency-Key</c> or a body that breaks its own rules,
    /// before the transaction is read; 404 <c>not-found</c> for a missing transaction or another customer's; 409
    /// <c>already-disputed</c> with the existing dispute's <c>disputeId</c> and <c>customerStatus</c> when it has ever
    /// been disputed; 422 <c>not-disputable</c> with <c>reason</c>; 400 <c>validation</c> for an expected amount not below
    /// the transaction amount; 503 <c>core-banking-unavailable</c>, with nothing written; 504 after 30 seconds.
    /// </summary>
    /// <remarks>
    /// The checks run 401, 403, 400, 404, 409, 422, 400 (AD-8): the IDs travel in the body, so built-in validation
    /// runs before the lookup. The <c>Linked</c> policy runs in the authorization middleware, before binding.
    /// Idempotent under <c>Idempotency-Key</c> (AD-16): the same request with the same key gets the stored 201 or 4xx
    /// again, byte for byte; a different request under the key gets 422 <c>idempotency-mismatch</c>, and one sent while
    /// the first is still running gets 409 <c>idempotency-in-progress</c>. A 5xx releases the key. Keys are scoped to
    /// the customer and kept for 24 hours.
    /// </remarks>
    private static async Task<IResult> RaiseAsync(
        RaiseDisputeRequest request,
        RaiseDisputeHandler handler,
        ClaimedIdempotencyKey claimed,
        RaiseMetrics metrics,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        Result<DisputeResponse> result = await handler.HandleAsync(request, claimed.Key, cancellationToken);
        if (result.IsFailure)
        {
            metrics.RecordRefusal(ErrorResults.StatusCodeFor(result.Error.Kind));
            return result.Error.ToProblem();
        }

        metrics.RecordRaised(result.Value.Reason);
        // The 201 committed with the dispute, written from that stored copy so a replay is the same bytes.
        return StoredResponse.From(claimed.Key, response);
    }

    /// <summary>
    /// <see cref="IdempotencyFilter"/> reads the header itself, so the document declares it here, required, and the
    /// generated client sends it.
    /// </summary>
    private static Task DocumentIdempotencyKeyAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IdempotencyKeyHeader.Name,
            In = ParameterLocation.Header,
            Required = true,
            Description = "An RFC 9651 String, so quoted, such as \"8e03978e-40d5-43e8-bc93-6894a57f9324\": 1 to 255 printable "
                + "characters once decoded. One key per raise attempt; resend the same key to retry it.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });
        return Task.CompletedTask;
    }
}
