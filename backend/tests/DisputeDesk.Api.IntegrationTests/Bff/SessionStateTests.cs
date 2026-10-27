using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;

namespace DisputeDesk.Api.IntegrationTests.Bff;

/// <summary>AC4 and AC9 (story 2.2), AC9 (story 2.3): GET /api/v1/bff/session answers the caller's state as a camelCase enum value.</summary>
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
    public async Task Session_UnlinkedLogin_ReturnsUnlinked()
    {
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");

        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/bff/session");

        Assert.Equal("""{"state":"unlinked"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Session_LinkedLogin_ReturnsLinked()
    {
        // AC9 (story 2.3): the state comes from CustomerLinks by the session's sub.
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        await factory.LinkAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-a");

        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/bff/session");

        Assert.Equal("""{"state":"linked"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
