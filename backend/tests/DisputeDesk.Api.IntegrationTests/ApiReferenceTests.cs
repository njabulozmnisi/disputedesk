using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>Scalar and the runtime OpenAPI document exist in Development only.</summary>
public class ApiReferenceTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task ApiReference_Development_IsServed(string path)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Development");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task ApiReference_Production_Returns404(string path)
    {
        // Signed in: an anonymous request that matches no endpoint gets the fallback policy's 401 instead (2.8 AC9).
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Production");
        using HttpClient client = factory.CreateBrowser(new CookieContainer());
        await client.SignInAsync(factory.Zitadel, $"api-reference-{Guid.NewGuid():N}");

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task ApiReference_ProductionAnonymous_Returns401(string path)
    {
        // No endpoint matches, so the authorization middleware applies the fallback policy: 401 (2.8 AC9).
        await using var factory = new ApiFactory(postgres.ConnectionString, environment: "Production");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
