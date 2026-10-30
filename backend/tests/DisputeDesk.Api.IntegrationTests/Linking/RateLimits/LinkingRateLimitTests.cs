using System.Net;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;

namespace DisputeDesk.Api.IntegrationTests.Linking.RateLimits;

/// <summary>
/// 6.3 AC1: start and verify requests share a 5-per-login and a 20-per-client-IP sliding window, the IP limiter chained
/// before the login limiter; only the linking routes are limited, matched by endpoint, after authorization.
/// </summary>
public class LinkingRateLimitTests(PostgresFixture postgres)
{
    private static readonly IPAddress OtherIp = IPAddress.Parse("198.51.100.20");

    [Fact]
    public async Task Limits_LoginSendsASixthStartOrVerify_Gets429WhileAnotherLoginFromTheSameIpIsNotLimited()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("login-limit"));

        // Starts and verifies share the login's five permits, and a 400 takes one like any other answer.
        Assert.Equal(HttpStatusCode.Accepted, (await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch), withAntiforgeryToken: false)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.VerifyCodeAsync("123456")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch))).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await browser.VerifyCodeAsync("123456")).StatusCode);
        using HttpClient other = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("login-limit-other"));
        Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(other)).StatusCode);
    }

    [Fact]
    public async Task Limits_FifthLoginSendsTheTwentyFirstRequestFromOneIp_Gets429AndKeepsItsLoginPermits()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        for (int login = 0; login < 4; login++)
        {
            using HttpClient atLimit = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("ip-limit"));
            await SendAcceptedAsync(atLimit, 5);
        }

        using HttpClient fifth = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("ip-limit-fifth"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(fifth)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await fifth.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch))).StatusCode);
        // Only the linking routes are limited.
        Assert.Equal(HttpStatusCode.OK, (await fifth.GetPathAsync("/api/v1/bff/session")).StatusCode);

        // The IP limiter rejected those two before the login limiter ran, so the login still has all five permits.
        api.ClientAddress = OtherIp;
        await SendAcceptedAsync(fifth, 5);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(fifth)).StatusCode);
    }

    [Fact]
    public async Task Limits_RequestTheLoginLimitRejects_HasAlreadySpentIpPermits()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        using HttpClient first = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("edge-first"));
        await SendAcceptedAsync(first, 5);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(first)).StatusCode);

        // The middleware asks the global limiter twice before it rejects (AttemptAcquire, then AcquireAsync), and the IP
        // limiter grants both times before the login limiter refuses: 7 IP permits are spent, and 13 more reach 20.
        for (int login = 0; login < 2; login++)
        {
            using HttpClient full = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("edge-full"));
            await SendAcceptedAsync(full, 5);
        }

        using HttpClient last = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("edge-last"));
        await SendAcceptedAsync(last, 3);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(last)).StatusCode);
    }

    [Fact]
    public async Task Limits_IpPartitions_IPv4MappedIsTheIPv4AddressAndIPv6IsItsSlash64()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);

        // ApiFactory's address is ::ffff:203.0.113.7; the plain IPv4 address is the same partition.
        await FillIpAsync(api, ApiFactory.ClientAddress);
        Assert.Equal(HttpStatusCode.TooManyRequests, await FirstRequestFromAsync(api, IPAddress.Parse("203.0.113.7")));
        Assert.Equal(HttpStatusCode.BadRequest, await FirstRequestFromAsync(api, IPAddress.Parse("203.0.113.8")));

        await FillIpAsync(api, IPAddress.Parse("2001:db8:1:2::1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await FirstRequestFromAsync(api, IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff")));
        Assert.Equal(HttpStatusCode.BadRequest, await FirstRequestFromAsync(api, IPAddress.Parse("2001:db8:1:3::1")));
    }

    [Fact]
    public async Task Limits_NoClientIpAfterAnUnlimitedRoute_IsStillOneIpPartition()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("no-ip-after"));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetPathAsync("/api/v1/bff/session")).StatusCode);

        // Requests with no client IP share one partition, whatever route was seen first.
        await FillIpAsync(api, null);
        Assert.Equal(HttpStatusCode.TooManyRequests, await FirstRequestFromAsync(api, null));
    }

    [Fact]
    public async Task Limits_UpperCasePathToTheStartRoute_IsLimited()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("upper-case"));
        await SendAcceptedAsync(browser, 4);

        Assert.Equal(HttpStatusCode.Accepted, await StartUpperCaseAsync(browser));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartUpperCaseAsync(browser));
    }

    [Fact]
    public async Task Limits_AnonymousAndLinkedRequests_GetTheir401And403AndTakeNoIpPermit()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        using HttpClient anonymous = api.Factory.CreateBrowser(new CookieContainer());
        string linkedLogin = LinkingApi.NewLogin("linked");
        await api.Factory.LinkAsync(linkedLogin, LinkingFakes.D.CustomerId);
        using HttpClient linked = await api.Factory.SignInUnlinkedAsync(linkedLogin);

        for (int request = 0; request < 25; request++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch), withAntiforgeryToken: false)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await RateLimitApi.MalformedVerifyAsync(linked)).StatusCode);
        }

        // The IP still has all 20 permits.
        await FillIpAsync(api, ApiFactory.ClientAddress);
        Assert.Equal(HttpStatusCode.TooManyRequests, await FirstRequestFromAsync(api, ApiFactory.ClientAddress));
    }

    /// <summary>Sends <paramref name="count"/> malformed verifies, each of which must pass the limits (400).</summary>
    private static async Task SendAcceptedAsync(HttpClient browser, int count)
    {
        for (int request = 0; request < count; request++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        }
    }

    /// <summary>Spends all 20 permits of <paramref name="address"/>'s partition, from four logins at their limit.</summary>
    private static async Task FillIpAsync(RateLimitApi api, IPAddress? address)
    {
        api.ClientAddress = address;
        for (int login = 0; login < 4; login++)
        {
            using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("fill"));
            await SendAcceptedAsync(browser, 5);
        }
    }

    /// <summary>The status of one malformed verify by a new login from <paramref name="address"/>.</summary>
    private static async Task<HttpStatusCode> FirstRequestFromAsync(RateLimitApi api, IPAddress? address)
    {
        api.ClientAddress = address;
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("first"));
        return (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode;
    }

    private static async Task<HttpStatusCode> StartUpperCaseAsync(HttpClient browser)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(LinkingApi.StartPath.ToUpperInvariant(), UriKind.Relative))
        {
            Content = new StringContent(LinkingApi.Body(LinkingFakes.NoMatch), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        return (await browser.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode;
    }
}
