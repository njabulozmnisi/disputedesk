using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputeDesk.Api.Disputes;

/// <summary><c>POST /api/v1/disputes</c>, on the customer routes.</summary>
public static class DisputeEndpoints
{
    public static IServiceCollection AddDisputes(this IServiceCollection services) =>
        services.AddScoped<RaiseDisputeHandler>()
            .AddSingleton<RaiseMetrics>();

    public static RouteGroupBuilder MapDisputes(this RouteGroupBuilder customerRoutes)
    {
        customerRoutes.MapPost("/disputes", RaiseAsync)
            .WithTags("Disputes")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return customerRoutes;
    }

    /// <summary>
    /// Raises a dispute on one of the linked customer's transactions. 201 with the dispute and its <c>Location</c>;
    /// 400 <c>validation</c> for a body that breaks its own rules, before the transaction is read; 404 <c>not-found</c>
    /// for a missing transaction or another customer's; 409 <c>already-disputed</c> when it has ever been disputed; 422
    /// <c>not-disputable</c> with <c>reason</c>; 400 <c>validation</c> for an expected amount not below the transaction
    /// amount; 503 <c>core-banking-unavailable</c>, with nothing written.
    /// </summary>
    /// <remarks>
    /// The checks run 401, 403, 400, 404, 409, 422, 400 (AD-8): the IDs travel in the body, so built-in validation
    /// runs before the lookup. The <c>Linked</c> policy runs in the authorization middleware, before binding.
    /// </remarks>
    private static async Task<Results<Created<DisputeResponse>, ProblemHttpResult>> RaiseAsync(
        RaiseDisputeRequest request,
        RaiseDisputeHandler handler,
        RaiseMetrics metrics,
        CancellationToken cancellationToken)
    {
        Result<DisputeResponse> result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            metrics.RecordRefusal(ErrorResults.StatusCodeFor(result.Error.Kind));
            return result.Error.ToProblem();
        }

        metrics.RecordRaised(result.Value.Reason);
        // shortcut: the GET this names is built in story 3.3 (a known gap against AD-5); 3.3 proves it serves the dispute
        return TypedResults.Created($"/api/v1/disputes/{result.Value.DisputeId}", result.Value);
    }
}
