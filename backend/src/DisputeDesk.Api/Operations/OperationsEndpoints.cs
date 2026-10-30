using DisputeDesk.Api.Disputes;
using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Operations.Actions;
using DisputeDesk.Application.Operations.ReadDispute;
using DisputeDesk.Contracts.Operations;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace DisputeDesk.Api.Operations;

/// <summary>
/// The operations routes under <c>/api/v1/operations</c>, for operations tools with a bearer token (FR-21 to FR-23,
/// FR-30). Mapped on the <c>/api/v1</c> group, never on the customer routes, so they carry no cookie scheme and no
/// antiforgery filter (AD-8). Each route has its own role policy (AD-9).
/// </summary>
public static class OperationsEndpoints
{
    // AD-3: names the field and never echoes its value.
    private static readonly Error DisputeIdNotAGuid = Error.Validation("validation", "The dispute ID must be a GUID.", "disputeId");

    public static IServiceCollection AddOperations(this IServiceCollection services) =>
        services.AddScoped<ReadDisputeHandler>()
            .AddScoped<OperationsActionHandler>();

    public static RouteGroupBuilder MapOperations(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder operations = apiV1.MapGroup("/operations")
            .WithTags("Operations")
            .AddEndpointFilter(NameTheUnboundDisputeIdAsync)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        operations.MapGet("/disputes/{disputeId}", ReadDisputeAsync)
            .RequireAuthorization(OperationsAuthentication.DisputeReadPolicy)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        operations.MapPost(OperationsRoutes.StartReview, StartReviewAsync)
            .RequireAuthorization(OperationsAuthentication.StartReviewPolicy)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        operations.MapPost(OperationsRoutes.Escalate, EscalateAsync)
            .RequireAuthorization(OperationsAuthentication.EscalatePolicy)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        operations.MapPost(OperationsRoutes.Resolve, ResolveAsync)
            .RequireAuthorization(OperationsAuthentication.ResolvePolicy)
            // The body is checked after the dispute is loaded, so 404 comes before 400 (AD-8 as amended 2026-10-30).
            .DisableValidation()
            // Routing turns a body that is not JSON into a 415 endpoint of its own, which carries no policy, so the
            // cookie-only fallback policy would answer a bearer caller 401. Accepting every content type at routing
            // (the last IAcceptsMetadata wins) leaves the 415 to the body binding, after this route's policy. Recorded
            // under AD-8: an empty content-type list matches any content type in aspnetcore
            // src/Http/Routing/src/Matching/AcceptsMatcherPolicy.cs (release/10.0), not on Microsoft Learn; pinned by
            // Resolve_TextPlainBodyWithNoTokenOrARefusedToken_Returns401BearerOr403Before415 and
            // Resolve_TextPlainBody_Returns415ValidationAndWritesNoRow.
            .WithMetadata(new AcceptsMetadata([], typeof(ResolveDisputeRequest)))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        return operations;
    }

    /// <summary>
    /// A <c>disputeId</c> that is not a GUID fails to bind, and the framework sets an empty 400 and runs the endpoint's
    /// filters without the handler (ThrowOnBadRequest is off, Program.cs). This replaces that body with the validation
    /// problem naming the field (Microsoft Learn, "Filters and parameter binding failures"). Any other binding failure,
    /// such as a resolve body, keeps the framework's response, which names no field.
    /// </summary>
    private static async ValueTask<object?> NameTheUnboundDisputeIdAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        bool bindingFailed = context.HttpContext.Response.StatusCode >= StatusCodes.Status400BadRequest;
        if (bindingFailed && !Guid.TryParse(context.HttpContext.Request.RouteValues["disputeId"] as string, out _))
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

    /// <summary>
    /// Starts reviewing a submitted dispute (FR-21). Needs the <c>dispute.startReview</c> role and a mapped token
    /// <c>sub</c>. 200 with the dispute as the operations read shows it; 404 <c>not-found</c> for an unknown dispute; 409
    /// <c>action-not-allowed</c> with <c>disputeStatus</c> in any other status. Takes no body; one sent is ignored.
    /// </summary>
    /// <remarks>The checks run 401, 403, 400 (a <paramref name="disputeId"/> that is not a GUID), 404, 409 (AD-8).</remarks>
    private static Task<Results<Ok<OperationsDisputeResponse>, ProblemHttpResult>> StartReviewAsync(
        Guid disputeId,
        OperationsActionHandler handler,
        DisputeActionMetrics metrics,
        CancellationToken cancellationToken) =>
        ActAsync(disputeId, DisputeAction.StartReview, "startReview", resolve: null, handler, metrics, cancellationToken);

    /// <summary>
    /// Escalates a submitted or under-review dispute (FR-22). Needs the <c>dispute.escalate</c> role and a mapped token
    /// <c>sub</c>. 200 with the dispute as the operations read shows it; 404 <c>not-found</c> for an unknown dispute; 409
    /// <c>action-not-allowed</c> with <c>disputeStatus</c> in any other status. Takes no body; one sent is ignored.
    /// </summary>
    /// <remarks>The checks run 401, 403, 400 (a <paramref name="disputeId"/> that is not a GUID), 404, 409 (AD-8).</remarks>
    private static Task<Results<Ok<OperationsDisputeResponse>, ProblemHttpResult>> EscalateAsync(
        Guid disputeId,
        OperationsActionHandler handler,
        DisputeActionMetrics metrics,
        CancellationToken cancellationToken) =>
        ActAsync(disputeId, DisputeAction.Escalate, "escalate", resolve: null, handler, metrics, cancellationToken);

    /// <summary>
    /// Resolves an under-review or escalated dispute with an outcome and a note for the customer (FR-23). Needs the
    /// <c>dispute.resolve</c> role and a mapped token <c>sub</c>. 200 with the dispute as the operations read shows it; 404
    /// <c>not-found</c> for an unknown dispute; 400 <c>validation</c> naming <c>outcome</c> or <c>note</c>; 409
    /// <c>action-not-allowed</c> with <c>disputeStatus</c> in any other status.
    /// </summary>
    /// <remarks>
    /// The checks run 401, 403, then 400 for a body or <paramref name="disputeId"/> that does not bind (415 for a body that
    /// is not JSON), 404, 400 for the outcome and the note, and last 409 (AD-8 as amended 2026-10-30).
    /// </remarks>
    private static Task<Results<Ok<OperationsDisputeResponse>, ProblemHttpResult>> ResolveAsync(
        Guid disputeId,
        ResolveDisputeRequest request,
        OperationsActionHandler handler,
        DisputeActionMetrics metrics,
        CancellationToken cancellationToken) =>
        ActAsync(disputeId, DisputeAction.Resolve, "resolve", request, handler, metrics, cancellationToken);

    /// <summary>Runs one action and counts it only once it has committed (AD-20).</summary>
    private static async Task<Results<Ok<OperationsDisputeResponse>, ProblemHttpResult>> ActAsync(
        Guid disputeId,
        DisputeAction action,
        string metricAction,
        ResolveDisputeRequest? resolve,
        OperationsActionHandler handler,
        DisputeActionMetrics metrics,
        CancellationToken cancellationToken)
    {
        Result<OperationsActionResult> result = await handler.HandleAsync(disputeId, action, resolve, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        OperationsDisputeResponse dispute = result.Value.Dispute;
        metrics.RecordOperationsAction(metricAction);
        if (action == DisputeAction.Resolve && dispute.Outcome is Contracts.Disputes.Outcome outcome)
        {
            metrics.RecordResolved(outcome, result.Value.SlaEscalated);
        }

        return TypedResults.Ok(dispute);
    }
}
