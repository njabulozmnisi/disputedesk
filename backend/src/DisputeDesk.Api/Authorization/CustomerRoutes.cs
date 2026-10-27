using DisputeDesk.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace DisputeDesk.Api.Authorization;

/// <summary>
/// The customer routes (AD-8, AD-10) and their <c>Linked</c> policy: the session cookie only, a signed-in login, and a
/// link to a customer. Anonymous gets 401 <c>unauthorized</c> and an unlinked login 403 <c>linking-required</c>,
/// never a redirect. Authorization runs before the endpoint binds or validates anything.
/// </summary>
public static class CustomerRoutes
{
    public const string LinkedPolicy = "Linked";

    public static IServiceCollection AddCustomerRoutes(this IServiceCollection services)
    {
        services.AddScoped<CurrentCustomer>();
        services.AddScoped<IAuthorizationHandler, LinkedCustomerHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, LinkingRequiredResultHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(LinkedPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new LinkedCustomerRequirement()));
        return services;
    }

    /// <summary>The group customer features map their endpoints onto.</summary>
    public static RouteGroupBuilder MapCustomerRoutes(this RouteGroupBuilder apiV1) =>
        apiV1.MapGroup("")
            .RequireAuthorization(LinkedPolicy)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
