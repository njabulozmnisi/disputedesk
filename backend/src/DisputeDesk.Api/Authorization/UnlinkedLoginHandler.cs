using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Api.Authorization;

/// <summary>
/// Checks on every linking request that the session's login (<c>sub</c>) has no row in <c>CustomerLinks</c> (AD-10). A
/// linked login leaves the requirement unmet, which <see cref="LinkingRequiredResultHandler"/> answers with 403
/// <c>already-linked</c>. Scoped, because it uses the request's <see cref="AppDbContext"/>.
/// </summary>
public sealed class UnlinkedLoginHandler(AppDbContext db) : AuthorizationHandler<UnlinkedLoginRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, UnlinkedLoginRequirement requirement)
    {
        // Handlers run even when authentication failed (no sub); the policy's authenticated-user requirement answers that with 401.
        string? loginSub = context.User.FindFirst("sub")?.Value;
        if (loginSub is null)
        {
            return;
        }

        bool isLinked = await db.Set<CustomerLink>().AnyAsync(link => link.LoginSub == loginSub);
        if (!isLinked)
        {
            context.Succeed(requirement);
        }
    }
}
