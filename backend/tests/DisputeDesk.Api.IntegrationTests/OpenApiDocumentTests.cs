using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// The OpenAPI document (the source of <c>openapi.json</c>): each enum component lists exactly its wire names, never
/// <c>null</c>, and the raise body documents the bounds its validation enforces.
/// </summary>
public class OpenApiDocumentTests(PostgresFixture postgres)
{
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

    private static string?[] EnumNames(JsonElement schemas, string component) =>
        [.. schemas.GetProperty(component).GetProperty("enum").EnumerateArray().Select(value => value.GetString())];

    private async Task<JsonDocument> GetDocumentAsync()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Development");
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync();
    }
}
