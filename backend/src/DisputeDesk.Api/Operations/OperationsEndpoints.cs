using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Operations.ReadDispute;
using DisputeDesk.Contracts.Operations;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputeDesk.Api.Operations;

/// <summary>
/// The operations routes under <c>/api/v1/operations</c>, for operations tools with a bearer token (FR-30). Mapped on the
/// <c>/api/v1</c> group, never on the customer routes, so they carry no cookie scheme and no antiforgery filter (AD-8).
/// </summary>
public static class OperationsEndpoints
{
    // AD-3: names the field and never echoes its value.
    private static readonly Error DisputeIdNotAGuid = Error.Validation("validation", "The dispute ID must be a GUID.", "disputeId");

    public static IServiceCollection AddOperations(this IServiceCollection services) =>
        services.AddScoped<ReadDisputeHandler>();

    public static RouteGroupBuilder MapOperations(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder operations = apiV1.MapGroup("/operations")
            .WithTags("Operations")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        operations.MapGet("/disputes/{disputeId}", ReadDisputeAsync)
            .RequireAuthorization(OperationsAuthentication.DisputeReadPolicy)
            .AddEndpointFilter(NameTheUnboundDisputeIdAsync)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return operations;
    }

    /// <summary>
    /// A <c>disputeId</c> that is not a GUID fails to bind, and the framework sets an empty 400 and runs the endpoint's
    /// filters without the handler (ThrowOnBadRequest is off, Program.cs). This replaces that body with the validation
    /// problem naming the field (Microsoft Learn, "Filters and parameter binding failures"). The route value is the
    /// endpoint's only bound input, so a failed parameter check can only be it.
    /// </summary>
    private static async ValueTask<object?> NameTheUnboundDisputeIdAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return DisputeIdNotAGuid.ToProblem();
        }

        return await next(context);
    }

    /// <summary>
    /// Any customer's dispute, with every stored event on its timeline oldest first. Needs the <c>dispute.read</c> role and
    /// a token <c>sub</c> mapped to the operations actor. 404 <c>not-found</c> for an unknown dispute; 400 <c>validation</c>
    /// for a <paramref name="disputeId"/> that is not a GUID. Every read, found or not, is audited.
    /// </summary>
    /// <remarks>
    /// The policy runs in the authorization middleware, before binding, so the checks run 401, 403, then 400 (AD-8).
    /// <paramref name="disputeId"/> has no route constraint on purpose, as on the customer detail: a value that is not a
    /// GUID fails to bind and gets 400, where a <c>:guid</c> constraint would make it 404.
    /// </remarks>
    private static async Task<Results<Ok<OperationsDisputeResponse>, ProblemHttpResult>> ReadDisputeAsync(
        Guid disputeId,
        ReadDisputeHandler handler,
        CancellationToken cancellationToken)
    {
        Result<OperationsDisputeResponse> result = await handler.HandleAsync(disputeId, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
