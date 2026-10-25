using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>Scalar and the runtime OpenAPI document exist in Development only.</summary>
public class ApiReferenceTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task Api_reference_is_served_in_Development(string path)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task Api_reference_is_absent_in_Production(string path)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
