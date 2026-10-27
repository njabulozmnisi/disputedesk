using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests.Bff;

/// <summary>AC4 and AC9: GET /api/v1/bff/session answers the caller's state as a camelCase enum value.</summary>
public class SessionStateTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Session_Anonymous_ReturnsAnonymous()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/bff/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"state":"anonymous"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Session_SignedInLogin_ReturnsUnlinked()
    {
        // No login is linked until the linking story adds CustomerLinks; then a linked login answers "linked".
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel);

        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/bff/session");

        Assert.Equal("""{"state":"unlinked"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
