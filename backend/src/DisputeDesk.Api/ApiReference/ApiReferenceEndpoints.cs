using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

namespace DisputeDesk.Api.ApiReference;

/// <summary>
/// The OpenAPI document (built in, and written to <c>openapi.json</c> at build time) and the Scalar page that renders it
/// at <c>/scalar/</c>, the stand-in operations tool (NFR-DOC-4, AD-26). A person pastes a bearer token from
/// <c>scripts/operations-token.sh</c> into Scalar; the page holds no credential and no configured value.
/// </summary>
public static class ApiReferenceEndpoints
{
    public static IServiceCollection AddApiReference(this IServiceCollection services)
    {
        services.AddOptions<ApiReferenceOptions>()
            .BindConfiguration(ApiReferenceOptions.Section)
            .ValidateOnStart();
        services.AddOpenApi(options => options
            .AddSchemaTransformer<EnumSchemaTransformer>()
            .AddDocumentTransformer<BearerSecuritySchemeTransformer>()
            .AddOperationTransformer<OperationsSecurityRequirementTransformer>());
        return services;
    }

    /// <summary>
    /// Maps the runtime document and Scalar, both public (AD-10's fallback policy covers the rest), only when
    /// <c>Scalar:Enabled</c> is true. The options are the same in every environment, so the page's one inline script has
    /// one fixed hash, which the gateway's Scalar CSP allows (<c>infra/nginx/templates/scalar-headers.conf.template</c>);
    /// changing any option changes that hash, and <c>ApiReferenceTests</c> then fails until the template is updated.
    /// </summary>
    public static void MapApiReference(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<IOptions<ApiReferenceOptions>>().Value.Enabled)
        {
            return;
        }

        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference(scalar => scalar
            .DisableDefaultFonts()     // its default loads fonts from a CDN, which font-src 'self' blocks
            .DisableTelemetry()
            .DisableAgent()            // the AI chat, on by default on localhost, talks to Scalar's servers
            .DisableMcp()
            .HideDeveloperTools())
            .AllowAnonymous();
    }
}
