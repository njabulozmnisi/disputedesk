using DisputeDesk.Api.Disputes.Idempotency;
using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.OpenApi;
using OpenApiOperationTransformerContext = Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext;

namespace DisputeDesk.Api.Disputes;

/// <summary><c>POST /api/v1/disputes</c>, on the customer routes.</summary>
public static class DisputeEndpoints
{
    /// <summary>
    /// The raise's request timeout policy, 30 seconds (AD-16): shorter than the 60 seconds after which another request
    /// may take an idempotency key over, so a live raise is never taken over. A named policy so tests can shorten it.
    /// </summary>
    public const string RaiseTimeoutPolicy = "raise";

    public static IServiceCollection AddDisputes(this IServiceCollection services) =>
        services.AddScoped<RaiseDisputeHandler>()
            .AddScoped<ClaimedIdempotencyKey>()
            .AddSingleton<RaiseMetrics>()
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
        return customerRoutes;
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
