using DisputeDesk.Application.Abstractions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Api.Authorization;

/// <summary>
/// Resolves the customer on every customer request (AD-10): looks the session's login (<c>sub</c>) up in
/// <c>CustomerLinks</c>, sets <see cref="CurrentCustomer"/> and succeeds. A login with no link leaves the requirement
/// unmet, which <see cref="AuthorizationResultHandler"/> answers with 403 <c>linking-required</c>. Nothing in the
/// request, and no claim but the login's <c>sub</c>, decides which customer it is. Scoped, because it uses the
/// request's <see cref="AppDbContext"/> and <see cref="CurrentCustomer"/>.
/// </summary>
public sealed class LinkedCustomerHandler(AppDbContext db, CurrentCustomer currentCustomer) : AuthorizationHandler<LinkedCustomerRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, LinkedCustomerRequirement requirement)
    {
        // Handlers run even when authentication failed (no sub); the policy's authenticated-user requirement answers that with 401.
        string? loginSub = context.User.FindFirst("sub")?.Value;
        if (loginSub is null)
        {
            return;
        }

        string? customerId = await db.Set<CustomerLink>()
            .Where(link => link.LoginSub == loginSub)
            .Select(link => link.CustomerId)
            .SingleOrDefaultAsync();
        if (customerId is null)
        {
            return;
        }

        currentCustomer.Set(customerId, loginSub);
        context.Succeed(requirement);
    }
}
