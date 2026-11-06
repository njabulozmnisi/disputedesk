using System.Text.Json;
using DisputeDesk.Api.Authentication;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests.Authentication;

/// <summary>
/// The cookie settings live under <c>Authentication</c>, the section ASP.NET Core also reads for
/// <c>Authentication:DefaultScheme</c> and <c>Authentication:Schemes:&lt;scheme&gt;</c> (Microsoft Learn, Configure JWT
/// bearer authentication). The section holds only the cookie keys, so it never sets a framework default by accident.
/// </summary>
public class BrowserCookieOptionsTests
{
    private static readonly string[] CookieKeys = ["AntiforgeryCookie", "Cookie", "CorrelationCookie", "NonceCookie", "SessionCookie"];

    [Fact]
    public void AppSettings_AuthenticationSection_HoldsOnlyTheCookieKeys()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Backend, "src", "DisputeDesk.Api", "appsettings.json")));

        Assert.Equal(CookieKeys, settings.RootElement.GetProperty(BrowserCookieOptions.Section).EnumerateObject().Select(key => key.Name).Order());
    }

    [Fact]
    public void BrowserCookieOptions_Properties_AreOnlyTheCookieKeys()
    {
        Assert.Equal(CookieKeys, typeof(BrowserCookieOptions).GetProperties().Select(property => property.Name).Order());
    }
}
