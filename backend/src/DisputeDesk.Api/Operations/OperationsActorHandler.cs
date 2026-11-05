using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain.Disputes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Operations;

/// <summary>
/// Resolves the operations actor on every operations request (AD-9, AD-10), as <c>LinkedCustomerHandler</c> resolves the
/// customer: a token whose <c>sub</c> is in <c>Operations:AllowedClients</c>, compared exactly (ordinal), is an
/// operations actor with that <c>sub</c> as its ID; the handler sets <see cref="CurrentOperationsActor"/> and succeeds.
/// Any other <c>sub</c>, such as a customer's login, leaves the requirement unmet, which is 403. Scoped, because it fills
/// the request's <see cref="CurrentOperationsActor"/>.
/// </summary>
public sealed class OperationsActorHandler(IOptions<OperationsOptions> options, CurrentOperationsActor currentActor)
    : AuthorizationHandler<OperationsActorRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationsActorRequirement requirement)
    {
        // Handlers run even when authentication failed (no sub); the policy's authenticated-user requirement answers that
        // with 401. A blank sub is refused before the lookup, so a blank entry in the list matches nothing.
        string? sub = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(sub))
        {
            return Task.CompletedTask;
        }

        if (!options.Value.AllowedClients.Contains(sub, StringComparer.Ordinal))
        {
            return Task.CompletedTask;
        }

        currentActor.Set(new Actor(ActorType.Operations, sub));
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
