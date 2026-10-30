using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain.Disputes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Operations;

/// <summary>
/// Resolves the operations actor on every operations request (AD-9, AD-10), as <c>LinkedCustomerHandler</c> resolves the
/// customer: looks the token's <c>sub</c> up in <c>Operations:Actors</c>, sets <see cref="CurrentOperationsActor"/> and
/// succeeds. An unmapped <c>sub</c>, such as a customer's login, leaves the requirement unmet, which is 403. Scoped,
/// because it fills the request's <see cref="CurrentOperationsActor"/>.
/// </summary>
public sealed class OperationsActorHandler(IOptions<OperationsOptions> options, CurrentOperationsActor currentActor)
    : AuthorizationHandler<OperationsActorRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationsActorRequirement requirement)
    {
        // Handlers run even when authentication failed (no sub); the policy's authenticated-user requirement answers that with 401.
        string? sub = context.User.FindFirst("sub")?.Value;
        if (sub is null || !options.Value.Actors.ContainsKey(sub))
        {
            return Task.CompletedTask;
        }

        // Start-up refused any mapped value but "operations" (OperationsOptions), so a mapped sub is an operations actor.
        currentActor.Set(new Actor(ActorType.Operations, sub));
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
