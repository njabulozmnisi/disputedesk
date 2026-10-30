using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputeDesk.Api.ApiReference;

/// <summary>
/// Declares the document's one security scheme, <c>bearer</c>: an HTTP bearer JWT (OpenAPI 3.1 Security Scheme Object;
/// Microsoft Learn, "Customize OpenAPI documents", its bearer sample). Scalar shows it as a field to paste a token into.
/// No OAuth2 scheme and no token URL: the token comes from <c>scripts/operations-token.sh</c> (decision 2026-10-30).
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            },
        };
        return Task.CompletedTask;
    }
}
