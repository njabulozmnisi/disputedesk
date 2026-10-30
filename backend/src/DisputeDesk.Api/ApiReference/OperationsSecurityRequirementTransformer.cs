using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputeDesk.Api.ApiReference;

/// <summary>
/// Puts the <c>bearer</c> requirement on every operation under <c>/api/v1/operations/</c> and on no other (OpenAPI 3.1
/// Security Requirement Object; Microsoft Learn, "Customize OpenAPI documents", operation transformers). Only those
/// routes take a bearer token (AD-8); the customer routes use the session cookie, which the document does not describe.
/// </summary>
internal sealed class OperationsSecurityRequirementTransformer : IOpenApiOperationTransformer
{
    // RelativePath has no leading slash.
    private const string OperationsPathPrefix = "api/v1/operations/";

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.RelativePath is { } path && path.StartsWith(OperationsPathPrefix, StringComparison.Ordinal))
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName, context.Document)] = [],
                },
            ];
        }

        return Task.CompletedTask;
    }
}
