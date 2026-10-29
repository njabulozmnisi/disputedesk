using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Bff;

/// <summary>
/// The BFF session model (AD-7): the Api is Zitadel's confidential client through the built-in OpenID Connect
/// handler, and the built-in cookie handler keeps a server-side session. The browser only ever holds a cookie.
/// </summary>
public static partial class BffAuthentication
{
    public static IServiceCollection AddBff(this IServiceCollection services)
    {
        OptionsBuilder<ZitadelOptions> zitadelSettings = services.AddOptions<ZitadelOptions>().BindConfiguration(ZitadelOptions.Section).ValidateDataAnnotations();
        OptionsBuilder<BffOptions> bffSettings = services.AddOptions<BffOptions>().BindConfiguration(BffOptions.Section).ValidateDataAnnotations();
        IDataProtectionBuilder dataProtection = services.AddDataProtection().SetApplicationName("DisputeDesk");
        services.AddSingleton<SessionTicketStore>();
        services.AddSingleton<ExpiredSessionCleanup>();

        if (BuildTimeOpenApi.IsRunning)
        {
            // No database while the build writes openapi.json, so the keys stay in memory for that run.
            dataProtection.UseEphemeralDataProtectionProvider();
        }
        else
        {
            zitadelSettings.ValidateOnStart();
            bffSettings.ValidateOnStart();
            // One key ring in Postgres, so every Api instance can read every session and antiforgery cookie.
            dataProtection.PersistKeysToDbContext<AppDbContext>();
            services.AddHostedService(provider => provider.GetRequiredService<ExpiredSessionCleanup>());
        }

        // The cookie scheme authenticates and challenges every request; only GET /api/v1/bff/login challenges Zitadel.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie()
            .AddOpenIdConnect();
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<BffOptions>, SessionTicketStore>(ConfigureCookie);
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<ZitadelOptions>>(ConfigureOpenIdConnect);
        // Deny by default (AD-10): an endpoint that names no policy, and a request that matches no endpoint, need a
        // signed-in user. Public endpoints opt out with AllowAnonymous().
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddAntiforgery();
        services.AddOptions<AntiforgeryOptions>().Configure<IOptions<BffOptions>>((antiforgery, bff) =>
        {
            antiforgery.HeaderName = "X-XSRF-TOKEN";
            antiforgery.Cookie.Name = bff.Value.AntiforgeryCookie.Name;
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;
            antiforgery.Cookie.SecurePolicy = bff.Value.Cookie.SecurePolicy;
            // nginx sets X-Frame-Options on every response; the Api adding its own would send the header twice.
            antiforgery.SuppressXFrameOptionsHeader = true;
        });

        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie, IOptions<BffOptions> bff, SessionTicketStore store)
    {
        cookie.Cookie.Name = bff.Value.SessionCookie.Name;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.SecurePolicy = bff.Value.Cookie.SecurePolicy;
        cookie.SessionStore = store;
        cookie.ExpireTimeSpan = SessionTicketStore.IdleTimeout;
        // Renewed on every authenticated request below, not by the built-in half-window rule.
        cookie.SlidingExpiration = false;

        cookie.Events.OnValidatePrincipal = context =>
        {
            context.ShouldRenew = true;
            return Task.CompletedTask;
        };
        // The Api never redirects to a sign-in page: a missing session is 401 and a refused one 403, and
        // UseStatusCodePages writes the unauthorized or forbidden problem body.
        cookie.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        cookie.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions oidc, IOptions<ZitadelOptions> zitadel)
    {
        oidc.Authority = zitadel.Value.Issuer;
        oidc.MetadataAddress = zitadel.Value.MetadataAddress;
        oidc.RequireHttpsMetadata = zitadel.Value.RequireHttpsMetadata;
        oidc.ClientId = zitadel.Value.Bff.ClientId;
        oidc.ClientSecret = zitadel.Value.Bff.ClientSecret;

        // Code flow with PKCE (UsePkce is the default); claims come from the ID token as Zitadel names them.
        oidc.ResponseType = "code";
        oidc.Scope.Add("email");
        oidc.MapInboundClaims = false;
        oidc.CallbackPath = "/api/signin-oidc";
        oidc.SignedOutCallbackPath = "/api/signout-callback-oidc";
        // SaveTokens would also keep the access token; OnTokenValidated below keeps only the ID token.
        oidc.SaveTokens = false;
        // The correlation and nonce cookies keep the handler defaults (AD-24): SameSite=None and Secure, which
        // browsers accept on http://localhost too.

        oidc.Events.OnTokenValidated = context =>
        {
            // Zitadel's login already requires a verified email; the BFF checks again (AD-6).
            if (context.Principal?.FindFirst("email_verified")?.Value is not "true")
            {
                context.Fail("The login's email address is not verified.");
                return Task.CompletedTask;
            }

            // The ID token, and only it, goes into the server-side ticket: sign-out sends it as id_token_hint so
            // Zitadel ends its session without asking (AD-7). The access token is never stored.
            string? idToken = context.TokenEndpointResponse?.IdToken;
            if (idToken is not null)
            {
                context.Properties?.StoreTokens([new AuthenticationToken { Name = "id_token", Value = idToken }]);
            }

            return Task.CompletedTask;
        };
        // After the handler accepts the ticket and before the cookie handler writes the session. A failed save throws,
        // so the callback is a 500 and no session exists without its SignIn entry (AD-7).
        oidc.Events.OnTicketReceived = async context =>
        {
            string loginSub = context.Principal?.FindFirst("sub")?.Value ?? throw new InvalidOperationException("The signed-in principal has no sub claim.");
            IServiceProvider services = context.HttpContext.RequestServices;
            await RecordSignInOrOutAsync(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<TimeProvider>(), AuditAction.SignIn, loginSub, context.HttpContext.RequestAborted);
        };
        // The end-session request (OpenID Connect RP-Initiated Logout 1.0, section 2). The handler adds the state and the
        // post_logout_redirect_uri and the id_token_hint (the session's stored ID token, read through the request's cookie
        // authentication); client_id lets Zitadel check that URI when there is no hint (an anonymous logout).
        oidc.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            context.ProtocolMessage.ClientId = context.Options.ClientId;
            return Task.CompletedTask;
        };
        oidc.Events.OnRemoteFailure = context =>
        {
            // Any failed callback (unverified email, bad state, a refused client, the login cancelled) ends with no
            // session. The callback is a browser navigation, so it goes back to the SPA, which shows the message.
            ILogger logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BffAuthentication));
            LogSignInFailed(logger, context.Failure?.Message);
            context.Response.Redirect("/?signIn=failed");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Writes and saves one <c>SignIn</c> or <c>SignOut</c> entry for the login (AD-14a). The customer ID is the one
    /// <c>CustomerLinks</c> holds for the login, null when it is unlinked.
    /// </summary>
    internal static async Task RecordSignInOrOutAsync(AppDbContext db, TimeProvider time, AuditAction action, string loginSub, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        string? customerId = await db.Set<CustomerLink>()
            .Where(link => link.LoginSub == loginSub)
            .Select(link => link.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);
        AuditLog.Record(db, action, AuditResult.Succeeded, new Actor(ActorType.Customer, loginSub), now, customerId);
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed: {Reason}")]
    private static partial void LogSignInFailed(ILogger logger, string? reason);
}
