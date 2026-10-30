using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// The OpenAPI document (the source of <c>openapi.json</c>): each enum component lists exactly its wire names, never
/// <c>null</c>, the raise body documents the bounds its validation enforces, and the one <c>bearer</c> scheme covers
/// exactly the operations routes, in the runtime document and the committed one (story 5.5).
/// </summary>
public class OpenApiDocumentTests(PostgresFixture postgres)
{
    // OpenAPI 3.1 Path Item Object: only these fields are operations; the rest (summary, parameters, servers) are not.
    private static readonly string[] HttpMethodKeys = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    [Fact]
    public async Task Document_EnumComponents_ListTheirWireNamesAndNeverNull()
    {
        using JsonDocument document = await GetDocumentAsync();
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        List<JsonProperty> enums = [.. schemas.EnumerateObject().Where(schema => schema.Value.TryGetProperty("enum", out _))];

        Assert.Contains(enums, schema => schema.Name == "NotDisputableReason");
        Assert.All(enums, schema => Assert.All(
            schema.Value.GetProperty("enum").EnumerateArray(),
            value => Assert.Equal(JsonValueKind.String, value.ValueKind)));
        Assert.Equal(
            ["submitted", "underReview", "underReviewPrioritised", "resolved", "withdrawn"],
            EnumNames(schemas, "CustomerStatus"));
        Assert.Equal(
            ["unrecognised", "duplicateCharge", "wrongAmount", "notReceived", "cancelledSubscription", "refundNotProcessed"],
            EnumNames(schemas, "Reason"));
    }

    [Fact]
    public async Task Document_RaiseDisputeRequest_DocumentsTheBodyBounds()
    {
        using JsonDocument document = await GetDocumentAsync();
        JsonElement properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("RaiseDisputeRequest").GetProperty("properties");

        foreach (string id in new[] { "accountRef", "transactionId" })
        {
            Assert.Equal(1, properties.GetProperty(id).GetProperty("minLength").GetInt32());
            Assert.Equal(RaiseDisputeRequest.IdMaxLength, properties.GetProperty(id).GetProperty("maxLength").GetInt32());
        }

        JsonElement expectedAmount = properties.GetProperty("expectedAmount");
        Assert.Equal(RaiseDisputeRequest.AmountPattern, expectedAmount.GetProperty("pattern").GetString());
        Assert.Equal(1, expectedAmount.GetProperty("minLength").GetInt32());
    }

    [Fact]
    public async Task RuntimeDocument_SecurityScheme_IsOneBearerSchemeOnTheOperationsRoutesOnly()
    {
        using JsonDocument document = await GetDocumentAsync();

        AssertBearerOnOperationsOnly(document.RootElement);
    }

    [Fact]
    public void CommittedDocument_SecurityScheme_IsOneBearerSchemeOnTheOperationsRoutesOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Backend, "openapi.json")));

        AssertBearerOnOperationsOnly(document.RootElement);
    }

    /// <summary>
    /// OpenAPI 3.1: exactly one Security Scheme Object, <c>bearer</c> (HTTP bearer, JWT); each operation under
    /// <c>/api/v1/operations/</c> carries exactly <c>[{"bearer": []}]</c>; no other operation, and not the document
    /// itself, carries a Security Requirement. No OAuth2 scheme and no token URL (decision 2026-10-30).
    /// </summary>
    private static void AssertBearerOnOperationsOnly(JsonElement root)
    {
        JsonProperty scheme = Assert.Single(root.GetProperty("components").GetProperty("securitySchemes").EnumerateObject());
        Assert.Equal("bearer", scheme.Name);
        Assert.Equal(
            [("bearerFormat", "JWT"), ("scheme", "bearer"), ("type", "http")],
            scheme.Value.EnumerateObject().Select(field => (field.Name, field.Value.GetString())).Order());
        Assert.False(root.TryGetProperty("security", out _));
        Assert.DoesNotContain("tokenUrl", root.GetRawText(), StringComparison.Ordinal);

        var operations = root.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(field => HttpMethodKeys.Contains(field.Name))
                .Select(operation => (Path: path.Name, Operation: operation.Value)))
            .ToList();
        var underOperations = operations.Where(entry => entry.Path.StartsWith("/api/v1/operations/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(underOperations);
        Assert.All(underOperations, entry => Assert.Equal(
            """[{"bearer":[]}]""",
            JsonSerializer.Serialize(entry.Operation.GetProperty("security"))));
        Assert.All(operations.Except(underOperations), entry => Assert.False(
            entry.Operation.TryGetProperty("security", out _), $"{entry.Path} declares a security requirement."));
    }

    private static string?[] EnumNames(JsonElement schemas, string component) =>
        [.. schemas.GetProperty(component).GetProperty("enum").EnumerateArray().Select(value => value.GetString())];

    private async Task<JsonDocument> GetDocumentAsync()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Development", settings: ApiFactory.ApiReferenceOn);
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync();
    }
}
