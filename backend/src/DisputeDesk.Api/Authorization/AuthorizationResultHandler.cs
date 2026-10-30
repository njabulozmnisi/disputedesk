using DisputeDesk.Api.Errors;
using DisputeDesk.Api.Operations;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace DisputeDesk.Api.Authorization;

/// <summary>
/// The one <see cref="IAuthorizationMiddlewareResultHandler"/> (the authorization middleware resolves only one; Microsoft
/// Learn, "Customize the behavior of AuthorizationMiddleware"). It writes 403 <c>linking-required</c> when the
/// <c>Linked</c> policy failed because the login is not linked, 403 <c>already-linked</c> when the <c>Unlinked</c>
/// policy failed because it is, and audits every 403 of an operations policy as <c>Denied</c> before the default
/// handler writes it (AD-14a). Every other response is the default handler's, whose challenge and forbid go to each
/// scheme's events.
/// </summary>
public sealed class AuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly Error LinkingRequired = Error.Forbidden(LinkingProblems.LinkingRequired, "Link your login to your bank account first.");
    private static readonly Error AlreadyLinked = Error.Forbidden(LinkingProblems.AlreadyLinked, "Your login is already linked to your bank account.");

    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (Failed<LinkedCustomerRequirement>(authorizeResult))
        {
            await LinkingRequired.ToProblem().ExecuteAsync(context);
            return;
        }

        if (Failed<UnlinkedLoginRequirement>(authorizeResult))
        {
            await AlreadyLinked.ToProblem().ExecuteAsync(context);
            return;
        }

        // An operations policy is recognised by its actor requirement, never by the path. Forbidden means the bearer
        // token authenticated, so a 401 is never audited.
        OperationsActorRequirement? operations = policy.Requirements.OfType<OperationsActorRequirement>().FirstOrDefault();
        if (authorizeResult.Forbidden && operations is not null)
        {
            await RecordDeniedAsync(context, operations.Action);
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool Failed<TRequirement>(PolicyAuthorizationResult authorizeResult) =>
        authorizeResult.Forbidden
        && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<TRequirement>().Any() == true;

    /// <summary>
    /// One <c>Denied</c> entry, saved before the 403 is written; a failed save throws, which the exception handler
    /// answers with 500 (fail closed; OWASP Logging Cheat Sheet). The actor is the bearer token's <c>sub</c>, mapped or
    /// not (AD-14a): the policy's scheme is JwtBearer only, so <see cref="HttpContext.User"/> is the token's principal
    /// here, never the cookie's. The dispute ID is the route value when it is a GUID; nothing is looked up, so the 403
    /// is the same for every ID. The handler is a singleton, so the scoped services come from the request.
    /// </summary>
    private static async Task RecordDeniedAsync(HttpContext context, AuditAction action)
    {
        IAppDbContext db = context.RequestServices.GetRequiredService<IAppDbContext>();
        TimeProvider time = context.RequestServices.GetRequiredService<TimeProvider>();
        // A Zitadel access token always carries sub; an empty actor ID would only mean a token without one.
        string sub = context.User.FindFirst("sub")?.Value ?? "";
        Guid? disputeId = Guid.TryParse(context.Request.RouteValues["disputeId"] as string, out Guid parsed) ? parsed : null;

        AuditLog.Record(db, action, AuditResult.Denied, new Actor(ActorType.Operations, sub), time.GetUtcNow(), disputeId: disputeId);
        await db.SaveChangesAsync(context.RequestAborted);
    }
}
