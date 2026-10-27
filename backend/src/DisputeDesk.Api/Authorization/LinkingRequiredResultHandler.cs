using DisputeDesk.Api.Errors;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace DisputeDesk.Api.Authorization;

/// <summary>
/// Writes 403 <c>linking-required</c> when the <c>Linked</c> policy failed because the login is not linked, and
/// leaves every other result to the default handler (Microsoft Learn: "Customize the behavior of authorization
/// middleware"). The default handler's challenge and forbid go to the cookie events, which answer 401 and 403.
/// </summary>
public sealed class LinkingRequiredResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly Error LinkingRequired = Error.Forbidden(LinkingProblems.LinkingRequired, "Link your login to your bank account first.");

    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        bool isNotLinked = authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<LinkedCustomerRequirement>().Any() == true;
        if (isNotLinked)
        {
            await LinkingRequired.ToProblem().ExecuteAsync(context);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
