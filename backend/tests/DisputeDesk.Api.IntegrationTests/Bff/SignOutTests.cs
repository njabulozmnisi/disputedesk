using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace DisputeDesk.Api.IntegrationTests.Bff;

/// <summary>AC6: signing out ends the DisputeDesk session and sends the browser to end the Zitadel one.</summary>
public class SignOutTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Logout_SignedIn_DeletesTheRowClearsBothCookiesAndRedirectsToEndSession()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        string sub = $"signout-{Guid.NewGuid():N}";
        await browser.SignInAsync(factory.Zitadel, sub);
        string token = await browser.AntiforgeryTokenAsync();
        Assert.Contains(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.AntiforgeryCookieName);

        HttpResponseMessage response = await browser.LogoutAsync(token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri endSession = Assert.IsType<Uri>(response.Headers.Location);
        Assert.StartsWith(factory.Zitadel.Configuration.EndSessionEndpoint, endSession.ToString(), StringComparison.Ordinal);
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(endSession.Query);
        Assert.Equal(FakeZitadel.ClientId, query["client_id"].ToString());
        Assert.Equal("https://localhost/", query["post_logout_redirect_uri"].ToString());
        Assert.Equal(factory.Zitadel.LastIdToken, query["id_token_hint"].ToString());

        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.SessionCookieName);
        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.AntiforgeryCookieName);
        await using AppDbContext db = postgres.CreateDbContext();
        Assert.False(await db.Set<Session>().AnyAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Request_ReplayingTheCookieAfterLogout_Returns401()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        await browser.SignInAsync(factory.Zitadel);
        Cookie oldSession = cookies.GetAllCookies().Single(cookie => cookie.Name == ApiFactory.SessionCookieName);
        await browser.LogoutAsync(await browser.AntiforgeryTokenAsync());

        // An attacker who copied the cookie before sign-out sends it again.
        var replayed = new CookieContainer();
        replayed.Add(ApiFactory.BaseAddress, new Cookie(oldSession.Name, oldSession.Value, "/"));
        using HttpClient replay = factory.CreateBrowser(replayed);
        HttpResponseMessage response = await replay.GetPathAsync("/api/v1/bff/antiforgery");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_Anonymous_SkipsAntiforgeryAndRedirectsToEndSessionWithoutAHint()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await browser.LogoutAsync(antiforgeryToken: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri endSession = Assert.IsType<Uri>(response.Headers.Location);
        Assert.StartsWith(factory.Zitadel.Configuration.EndSessionEndpoint, endSession.ToString(), StringComparison.Ordinal);
        Assert.False(QueryHelpers.ParseQuery(endSession.Query).ContainsKey("id_token_hint"));
    }
}
