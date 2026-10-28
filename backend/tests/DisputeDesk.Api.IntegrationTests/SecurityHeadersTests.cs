using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// The Api's own response headers. The nginx gateway owns the browser security headers (CSP,
/// X-Content-Type-Options, Referrer-Policy and the rest), so the Api must not send them too: each header appears
/// once per response (AD-23).
/// </summary>
public class SecurityHeadersTests(PostgresFixture postgres)
{
    public static TheoryData<string, HttpStatusCode, bool> Responses => new()
    {
        { "/health/live", HttpStatusCode.OK, false },
        { "/api/v1/no-such-route", HttpStatusCode.NotFound, true },
        // The exception handler clears the response headers before writing the 500, so this case matters.
        { ThrowProbe.Path, HttpStatusCode.InternalServerError, true },
    };

    [Theory]
    [MemberData(nameof(Responses))]
    public async Task UseSecurityHeaders_AnyResponse_LeavesGatewayHeadersToTheGatewayAndKeepsApiNoStore(
        string path, HttpStatusCode expectedStatus, bool isApiResponse)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: ThrowProbe.Register);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertNoGatewayHeaders(response);
        if (isApiResponse)
        {
            Assert.Equal(["no-store"], response.Headers.GetValues("Cache-Control"));
        }
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
        AssertNoGatewayHeaders(response);
    }

    private static void AssertNoGatewayHeaders(HttpResponseMessage response)
    {
        Assert.False(response.Headers.Contains("X-Content-Type-Options"));
        Assert.False(response.Headers.Contains("Referrer-Policy"));
    }
}
