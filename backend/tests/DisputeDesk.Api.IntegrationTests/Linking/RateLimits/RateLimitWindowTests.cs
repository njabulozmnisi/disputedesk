using System.Diagnostics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Audit;

namespace DisputeDesk.Api.IntegrationTests.Linking.RateLimits;

/// <summary>
/// 6.3 AC1 and AC3 over real time: the sliding-window limiter and the cache read the system clock and take no
/// <see cref="TimeProvider"/>, so these tests wait, on windows of a few seconds with margins of half a second or more.
/// The partitioned limiter replenishes from a timer every 100 ms, so a segment can end up to 100 ms late each time.
/// </summary>
public class RateLimitWindowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Window_PermitSpentInTheFirstSegment_ComesBackWhenThatSegmentLeavesTheWindowAndNotBefore()
    {
        // A 6-second window in two 3-second segments: segment 0 leaves the window at about 6 s, segment 1 at about 9 s.
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres, window: "00:00:06", segments: "2");
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("segments"));
        var clock = Stopwatch.StartNew();

        Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        await DelayUntilAsync(clock, TimeSpan.FromSeconds(3.7));
        for (int request = 0; request < 4; request++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        await DelayUntilAsync(clock, TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);

        // Segment 0 has left the window: its one permit is back, and segment 1's four are not.
        await DelayUntilAsync(clock, TimeSpan.FromSeconds(7));
        Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(9), $"The test ran past segment 1's return: {clock.Elapsed}.");
    }

    [Fact]
    public async Task Audit_RejectionsOfOneLogin_OnlyTheFirstIsAuditedUntilAWindowAfterIt()
    {
        // One 4-second segment: the permits and the audit record both come back about 4 s after they were taken.
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres, window: "00:00:04", segments: "1");
        string login = LinkingApi.NewLogin("audit-window");
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(login);
        var clock = Stopwatch.StartNew();

        await RejectAsync(browser, rejections: 2);
        Assert.Equal(1, await api.Factory.CountAuditsAsync(login, AuditAction.LinkingRateLimited, AuditResult.Rejected));

        await DelayUntilAsync(clock, TimeSpan.FromSeconds(5));
        await RejectAsync(browser, rejections: 1);
        Assert.Equal(2, await api.Factory.CountAuditsAsync(login, AuditAction.LinkingRateLimited, AuditResult.Rejected));
    }

    /// <summary>Spends the login's five permits, then sends <paramref name="rejections"/> more, which must get 429.</summary>
    private static async Task RejectAsync(HttpClient browser, int rejections)
    {
        for (int request = 0; request < 5; request++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        }

        for (int request = 0; request < rejections; request++)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        }
    }

    private static async Task DelayUntilAsync(Stopwatch clock, TimeSpan at)
    {
        TimeSpan wait = at - clock.Elapsed;
        Assert.True(wait > TimeSpan.Zero, $"The test is already past {at}: {clock.Elapsed}.");
        await Task.Delay(wait, TestContext.Current.CancellationToken);
    }
}
