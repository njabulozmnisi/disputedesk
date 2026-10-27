using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests.Bff;

/// <summary>
/// AC5: an unsafe request from a signed-in login needs the antiforgery token. POST /api/v1/bff/logout is the unsafe
/// cookie route this story has; customer and linking routes add the same <c>AntiforgeryFilter</c>.
/// </summary>
public class AntiforgeryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Logout_SignedInWithoutToken_Returns400Validation()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel);

        HttpResponseMessage response = await browser.LogoutAsync(antiforgeryToken: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/validation.md", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("unlinked", await browser.SessionStateAsync());
    }

    [Fact]
    public async Task Logout_SignedInWithForgedToken_Returns400WithoutEchoingIt()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel);

        HttpResponseMessage response = await browser.LogoutAsync("forged-token-marker");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("forged-token-marker", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_SignedInWithTokenFromAntiforgeryEndpoint_Passes()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel);
        string token = await browser.AntiforgeryTokenAsync();

        HttpResponseMessage response = await browser.LogoutAsync(token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Logout_TokenOfAnotherLogin_Returns400()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient attacker = factory.CreateBrowser(new CookieContainer());
        await attacker.SignInAsync(factory.Zitadel, "attacker");
        string attackersToken = await attacker.AntiforgeryTokenAsync();
        using HttpClient victim = factory.CreateBrowser(new CookieContainer());
        await victim.SignInAsync(factory.Zitadel, "victim");

        HttpResponseMessage response = await victim.LogoutAsync(attackersToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Antiforgery_Anonymous_Returns401Unauthorized()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/bff/antiforgery");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/unauthorized.md", problem.RootElement.GetProperty("type").GetString());
    }
}
