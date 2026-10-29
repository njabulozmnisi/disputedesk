using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// The Api's own response headers. The nginx gateway owns the browser security headers (CSP,
/// X-Content-Type-Options, Referrer-Policy and the rest), so the Api must not send them too: each header appears
/// once per response (AD-23). Every test runs in Development and in Production (2.8 AC10).
/// </summary>
public class SecurityHeadersTests(PostgresFixture postgres)
{
    public static TheoryData<string, bool, HttpStatusCode, bool, string> Responses()
    {
        var data = new TheoryData<string, bool, HttpStatusCode, bool, string>();
        foreach (string environment in new[] { "Development", "Production" })
        {
            data.Add("/health/live", false, HttpStatusCode.OK, false, environment);
            data.Add("/api/v1/no-such-route", true, HttpStatusCode.NotFound, true, environment);
            // The fallback policy answers an anonymous request with no endpoint before the probe runs (2.8 AC9).
            data.Add("/api/v1/no-such-route", false, HttpStatusCode.Unauthorized, true, environment);
            // The exception handler clears the response headers before writing the 500, so this case matters.
            data.Add(ThrowProbe.Path, true, HttpStatusCode.InternalServerError, true, environment);
            data.Add(ThrowProbe.Path, false, HttpStatusCode.Unauthorized, true, environment);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Responses))]
    public async Task UseSecurityHeaders_AnyResponse_LeavesGatewayHeadersToTheGatewayAndKeepsApiNoStore(
        string path, bool signedIn, HttpStatusCode expectedStatus, bool isUnderApiPath, string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, configureServices: ThrowProbe.Register);
        using HttpClient client = factory.CreateBrowser(new CookieContainer());
        if (signedIn)
        {
            await client.SignInAsync(factory.Zitadel, $"headers-{Guid.NewGuid():N}");
        }

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertNoGatewayHeaders(response);
        if (isUnderApiPath)
        {
            Assert.Equal(["no-store"], response.Headers.GetValues("Cache-Control"));
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Kestrel_AnyResponse_SendsNoServerHeader(string environment)
    {
        // TestServer never writes a Server header, so this runs the real Kestrel on a loopback port.
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);
        // The port in Kestrel's options, as CoreBankingStubFactory sets it: UseKestrel(0) races the app's own start.
        factory.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
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
