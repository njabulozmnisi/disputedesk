using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
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
        services.AddAuthorization();

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed: {Reason}")]
    private static partial void LogSignInFailed(ILogger logger, string? reason);
}
