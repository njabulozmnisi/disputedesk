using System.ComponentModel.DataAnnotations;
using DisputeDesk.Contracts.Bff;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace DisputeDesk.Api.Bff;

/// <summary>Sign-in, session state, the antiforgery token and sign-out (AD-7), under <c>/api/v1/bff</c>.</summary>
public static class BffEndpoints
{
    public static RouteGroupBuilder MapBff(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder bff = apiV1.MapGroup("/bff").WithTags("Bff");

        bff.MapGet("/login", LoginAsync).Produces(StatusCodes.Status302Found).ProducesValidationProblem();
        bff.MapGet("/session", Session);
        bff.MapGet("/antiforgery", Antiforgery).RequireAuthorization().ProducesProblem(StatusCodes.Status401Unauthorized);
        bff.MapPost("/logout", LogoutAsync).AddEndpointFilter<AntiforgeryFilter>()
            .Produces(StatusCodes.Status302Found).ProducesProblem(StatusCodes.Status400BadRequest);

        return bff;
    }

    /// <summary>Starts the code flow at Zitadel. After sign-in the browser returns to <paramref name="returnUrl"/> when it is a local path, otherwise to <c>/</c>.</summary>
    private static async Task<ChallengeHttpResult> LoginAsync(HttpContext http, [MaxLength(2048)] string? returnUrl)
    {
        // A new sign-in always gets a new session key (AD-7). Without this, the cookie handler would renew the old
        // session row with the new login's ticket.
        if (http.User.Identity?.IsAuthenticated == true)
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        string redirectUri = returnUrl is not null && RedirectHttpResult.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return TypedResults.Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static Ok<SessionResponse> Session(HttpContext http)
    {
        bool isSignedIn = http.User.Identity?.IsAuthenticated == true;
        // shortcut: a signed-in login is always unlinked until the linking story adds the CustomerLinks lookup.
        return TypedResults.Ok(new SessionResponse(isSignedIn ? SessionState.Unlinked : SessionState.Anonymous));
    }

    /// <summary>The request token for the <c>X-XSRF-TOKEN</c> header. It is bound to the login, so the SPA fetches it after sign-in.</summary>
    private static Ok<AntiforgeryResponse> Antiforgery(HttpContext http, IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);
        return TypedResults.Ok(new AntiforgeryResponse(tokens.RequestToken ?? ""));
    }

    /// <summary>
    /// A top-level form POST. Ends the DisputeDesk session (deletes the row the cookie names, clears the session and
    /// antiforgery cookies), then sends the browser to Zitadel's end-session endpoint to end that session too. A
    /// signed-in caller's ID token goes along as id_token_hint, so Zitadel ends its session without asking (AD-7).
    /// </summary>
    private static async Task<RedirectHttpResult> LogoutAsync(
        HttpContext http,
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        IOptions<BffOptions> bffOptions,
        IOptions<AntiforgeryOptions> antiforgeryOptions)
    {
        // Read before sign-out deletes the session that holds it. Null for an anonymous caller.
        string? idToken = await http.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, "id_token");

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // Deleted with the attributes it was set with: a __Host- cookie is only replaced by one that is also Secure.
        http.Response.Cookies.Delete(bffOptions.Value.AntiforgeryCookie.Name, antiforgeryOptions.Value.Cookie.Build(http));

        OpenIdConnectOptions oidc = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        IConfigurationManager<OpenIdConnectConfiguration> discovery = oidc.ConfigurationManager
            ?? throw new InvalidOperationException("The OpenID Connect handler has no configuration manager.");
        OpenIdConnectConfiguration configuration = await discovery.GetConfigurationAsync(http.RequestAborted);
        var endSessionQuery = new Dictionary<string, string?>
        {
            ["client_id"] = oidc.ClientId,
            ["post_logout_redirect_uri"] = $"{http.Request.Scheme}://{http.Request.Host}/",
        };
        if (idToken is not null)
        {
            endSessionQuery["id_token_hint"] = idToken;
        }

        return TypedResults.Redirect(QueryHelpers.AddQueryString(configuration.EndSessionEndpoint, endSessionQuery));
    }
}
