using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>The Api's own response headers. CSP is set by the nginx gateway, not here.</summary>
public class SecurityHeadersTests(PostgresFixture postgres)
{
    public static TheoryData<string, bool, HttpStatusCode> Responses => new()
    {
        { "/health/live", false, HttpStatusCode.OK },
        { "/api/v1/no-such-route", false, HttpStatusCode.NotFound },
        // The exception handler clears the response headers before writing the 500, so this case matters.
        { "/health/live", true, HttpStatusCode.InternalServerError },
    };

    [Theory]
    [MemberData(nameof(Responses))]
    public async Task UseSecurityHeaders_AnyResponse_SetsNosniffAndNoReferrer(string path, bool throwing, HttpStatusCode expectedStatus)
    {
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: throwing ? ThrowingHealthCheckService.Register : null);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Kestrel_AnyResponse_SendsNoServerHeader()
    {
        // TestServer never writes a Server header, so this runs the real Kestrel on a loopback port.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.Server);
        Assert.False(response.Headers.Contains("Server"));
        AssertSecurityHeaders(response);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(["no-referrer"], response.Headers.GetValues("Referrer-Policy"));
    }
}
