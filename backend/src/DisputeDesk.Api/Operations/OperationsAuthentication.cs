using System.Security.Claims;
using System.Text.Json;
using DisputeDesk.Api.Bff;
using DisputeDesk.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DisputeDesk.Api.Operations;

/// <summary>
/// The operations tools' credential (AD-8, AD-9): a Zitadel access token in the <c>Authorization</c> header, validated
/// by the built-in JwtBearer handler (Microsoft Learn, "Configure JWT bearer authentication in ASP.NET Core"), and one
/// policy per role that pins the JwtBearer scheme, so the session cookie never authenticates an operations route and the
/// token never authenticates any other. The cookie stays the default scheme.
/// </summary>
public static class OperationsAuthentication
{
    /// <summary>The read role, and the policy named after it (AD-9).</summary>
    public const string DisputeReadPolicy = "dispute.read";

    private const string RoleClaimType = "role";

    public static IServiceCollection AddOperationsAuthentication(this IServiceCollection services)
    {
        services.AddOptions<OperationsOptions>()
            .BindConfiguration(OperationsOptions.Section)
            .Validate(operations => operations.MapsOnlyToOperations(), "Every Operations:Actors value must be \"operations\" (AD-9).")
            .ValidateOnStart();
        services.AddScoped<CurrentOperationsActor>();
        services.AddScoped<IAuthorizationHandler, OperationsActorHandler>();

        // A second scheme beside the cookie; AddBff's AddAuthentication keeps the cookie as the default.
        services.AddAuthentication().AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ZitadelOptions>>(ConfigureJwtBearer);

        services.AddAuthorizationBuilder()
            .AddPolicy(DisputeReadPolicy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(DisputeReadPolicy)
                .AddRequirements(new OperationsActorRequirement()));
        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, IOptions<ZitadelOptions> zitadel)
    {
        // Signing keys come from Zitadel's discovery document, as for the BFF's OpenID Connect handler.
        jwt.Authority = zitadel.Value.Issuer;
        // Empty (the option's own unset value) derives the address from the authority.
        jwt.MetadataAddress = zitadel.Value.MetadataAddress ?? "";
        jwt.RequireHttpsMetadata = zitadel.Value.RequireHttpsMetadata;
        jwt.Audience = zitadel.Value.ProjectId;
        jwt.MapInboundClaims = false;
        // The challenge stays "WWW-Authenticate: Bearer" with no error_description naming why a token failed (RFC 6750 §3
        // makes it optional; ASVS 5.0 V16.5.1).
        jwt.IncludeErrorDetails = false;

        // Issuer, audience, lifetime (ClockSkew at its default of 5 minutes) and signature are validated by default.
        jwt.TokenValidationParameters.ValidIssuer = zitadel.Value.Issuer;
        jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
        jwt.TokenValidationParameters.RoleClaimType = RoleClaimType;

        string projectRolesClaim = $"urn:zitadel:iam:org:project:{zitadel.Value.ProjectId}:roles";
        jwt.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    FlattenProjectRoles(identity, projectRolesClaim);
                }

                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// Zitadel puts the granted roles in one claim whose value is an object keyed by role, for example
    /// <c>{"dispute.read": {"&lt;orgId&gt;": "&lt;domain&gt;"}}</c>. This adds one <c>role</c> claim per key of this project's
    /// claim only (AD-9): the generic <c>urn:zitadel:iam:org:project:roles</c> claim is not bound to this project, and a
    /// <c>role</c> claim the token carries itself is dropped, so no other claim can grant a role (ASVS 5.0 V10.3.2).
    /// </summary>
    private static void FlattenProjectRoles(ClaimsIdentity identity, string projectRolesClaim)
    {
        foreach (Claim tokenRole in identity.FindAll(RoleClaimType).ToList())
        {
            identity.RemoveClaim(tokenRole);
        }

        foreach (Claim roles in identity.FindAll(projectRolesClaim).Where(claim => claim.ValueType == JsonClaimValueTypes.Json).ToList())
        {
            using var grants = JsonDocument.Parse(roles.Value);
            foreach (JsonProperty role in grants.RootElement.EnumerateObject())
            {
                identity.AddClaim(new Claim(RoleClaimType, role.Name));
            }
        }
    }
}
