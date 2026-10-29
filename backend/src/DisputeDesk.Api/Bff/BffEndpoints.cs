using System.ComponentModel.DataAnnotations;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Bff;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Bff;

/// <summary>Sign-in, session state, the antiforgery token and sign-out (AD-7), under <c>/api/v1/bff</c>.</summary>
public static partial class BffEndpoints
{
    public static RouteGroupBuilder MapBff(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder bff = apiV1.MapGroup("/bff").WithTags("Bff");

        // Login, session and logout serve anonymous callers too; everything else falls under the fallback policy (AD-10).
        bff.MapGet("/login", LoginAsync).AllowAnonymous().Produces(StatusCodes.Status302Found).ProducesValidationProblem();
        bff.MapGet("/session", SessionAsync).AllowAnonymous();
        bff.MapGet("/antiforgery", Antiforgery).RequireAuthorization().ProducesProblem(StatusCodes.Status401Unauthorized);
        bff.MapPost("/logout", LogoutAsync).AllowAnonymous().AddEndpointFilter<AntiforgeryFilter>()
            .Produces(StatusCodes.Status302Found).ProducesProblem(StatusCodes.Status400BadRequest);

        return bff;
    }

    /// <summary>Starts the code flow at Zitadel. After sign-in the browser returns to <paramref name="returnUrl"/> when it is a local path, otherwise to <c>/</c>.</summary>
    private static async Task<ChallengeHttpResult> LoginAsync(
        HttpContext http, AppDbContext db, TimeProvider time, ILoggerFactory loggers, [MaxLength(2048)] string? returnUrl)
    {
        // A new sign-in always gets a new session key (AD-7). Without this, the cookie handler would renew the old
        // session row with the new login's ticket. Ending the old session is that session's sign-out.
        string? loginSub = http.User.Identity?.IsAuthenticated == true ? http.User.FindFirst("sub")?.Value : null;
        if (loginSub is not null)
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await RecordSignOutAsync(db, time, loggers, loginSub, http.RequestAborted);
        }

        string redirectUri = returnUrl is not null && RedirectHttpResult.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return TypedResults.Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    /// <summary>Anonymous without a session; otherwise linked when <c>CustomerLinks</c> holds the session's login, else unlinked (AD-10).</summary>
    private static async Task<Ok<SessionResponse>> SessionAsync(HttpContext http, AppDbContext db)
    {
        string? loginSub = http.User.Identity?.IsAuthenticated == true ? http.User.FindFirst("sub")?.Value : null;
        if (loginSub is null)
        {
            return TypedResults.Ok(new SessionResponse(SessionState.Anonymous));
        }

        bool isLinked = await db.Set<CustomerLink>().AnyAsync(link => link.LoginSub == loginSub, http.RequestAborted);
        return TypedResults.Ok(new SessionResponse(isLinked ? SessionState.Linked : SessionState.Unlinked));
    }

    /// <summary>The request token for the <c>X-XSRF-TOKEN</c> header. It is bound to the login, so the SPA fetches it after sign-in.</summary>
    private static Ok<AntiforgeryResponse> Antiforgery(HttpContext http, IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);
        return TypedResults.Ok(new AntiforgeryResponse(tokens.RequestToken ?? ""));
    }

    /// <summary>
    /// A top-level form POST. Ends the DisputeDesk session (deletes the row the cookie names, clears the session and
    /// antiforgery cookies), records <c>SignOut</c>, then the OpenID Connect handler sends the browser to Zitadel's
    /// end-session endpoint with a <c>state</c>, back through <c>/api/signout-callback-oidc</c> to <c>/</c>. A signed-in
    /// caller's ID token goes along as id_token_hint, so Zitadel ends its session without asking (AD-7).
    /// </summary>
    private static async Task<SignOutHttpResult> LogoutAsync(
        HttpContext http,
        AppDbContext db,
        TimeProvider time,
        ILoggerFactory loggers,
        IOptions<BffOptions> bffOptions,
        IOptions<AntiforgeryOptions> antiforgeryOptions)
    {
        // Null for an anonymous caller.
        string? loginSub = http.User.Identity?.IsAuthenticated == true ? http.User.FindFirst("sub")?.Value : null;

        // The session row goes first: if deleting it fails, the 500 leaves the login signed in with no SignOut entry,
        // so it can try again.
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // Deleted with the attributes it was set with: a __Host- cookie is only replaced by one that is also Secure.
        http.Response.Cookies.Delete(bffOptions.Value.AntiforgeryCookie.Name, antiforgeryOptions.Value.Cookie.Build(http));

        if (loginSub is not null)
        {
            await RecordSignOutAsync(db, time, loggers, loginSub, http.RequestAborted);
        }

        // The handler attaches the session's ID token as id_token_hint, read through this request's cookie authentication.
        return TypedResults.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    /// <summary>Records <c>SignOut</c> for a session that has just ended. A failed save is logged, not thrown: the session stays ended (AD-7; OWASP Session Management Cheat Sheet).</summary>
    private static async Task RecordSignOutAsync(AppDbContext db, TimeProvider time, ILoggerFactory loggers, string loginSub, CancellationToken cancellationToken)
    {
        try
        {
            await BffAuthentication.RecordSignInOrOutAsync(db, time, AuditAction.SignOut, loginSub, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSignOutAuditFailed(loggers.CreateLogger(typeof(BffEndpoints)), exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording the sign-out audit entry failed; the sign-out went ahead")]
    private static partial void LogSignOutAuditFailed(ILogger logger, Exception exception);
}
