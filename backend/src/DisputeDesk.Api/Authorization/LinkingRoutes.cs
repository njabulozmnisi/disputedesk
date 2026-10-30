using DisputeDesk.Api.Bff;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace DisputeDesk.Api.Authorization;

/// <summary>
/// The linking routes (AD-8, AD-10) and their <c>Unlinked</c> policy: the session cookie only, a signed-in login, and
/// no link to a customer. Anonymous gets 401 <c>unauthorized</c> and a linked login 403 <c>already-linked</c>, never a
/// redirect. Authorization runs before the endpoint binds or validates anything.
/// </summary>
public static class LinkingRoutes
{
    public const string UnlinkedPolicy = "Unlinked";

    public static IServiceCollection AddLinkingRoutes(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, UnlinkedLoginHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(UnlinkedPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new UnlinkedLoginRequirement()));
        return services;
    }

    /// <summary>
    /// The <c>/api/v1/linking</c> group. Every unsafe request on it needs the antiforgery token (AD-7). Request bodies
    /// are never logged here (AD-12); no request logging is registered at all.
    /// </summary>
    public static RouteGroupBuilder MapLinkingRoutes(this RouteGroupBuilder apiV1) =>
        apiV1.MapGroup("/linking")
            .WithTags("Linking")
            .RequireAuthorization(UnlinkedPolicy)
            .AddEndpointFilter<AntiforgeryFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
