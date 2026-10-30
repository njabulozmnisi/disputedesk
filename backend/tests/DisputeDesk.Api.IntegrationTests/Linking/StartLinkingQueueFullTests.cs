using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.5 AC4: a full linking queue answers every well-formed start 429 <c>rate-limited</c> with one body and no
/// <c>Retry-After</c>, audits <c>LinkingRateLimited</c> beside the start's own entry, and queues nothing.
/// </summary>
public class StartLinkingQueueFullTests(PostgresFixture postgres)
{
    private static readonly IPAddress StoredClientIp = IPAddress.Parse("203.0.113.7");

    // Each test Api has its own database, so the same filler logins serve every test.
    internal static readonly string[] FillerLogins = ["filler-0", "filler-1", "filler-2", "filler-3", "filler-4"];

    [Fact]
    public async Task Start_QueueFull_MatchAndNoMatchBothGetTheSame429WithoutRetryAfterAndAreAuditedNotQueued()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await StartApiWithAQueueOfOneAsync(ports, configureServices: null);
        LinkingFakes.Hold[] holds = await FillTheQueueAsync(factory, ports);
        string matchLogin = LinkingApi.NewLogin("full-match");
        string noMatchLogin = LinkingApi.NewLogin("full-no-match");
        using HttpClient matchBrowser = await factory.SignInUnlinkedAsync(matchLogin);
        using HttpClient noMatchBrowser = await factory.SignInUnlinkedAsync(noMatchLogin);

        HttpResponseMessage match = await matchBrowser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D));
        HttpResponseMessage noMatch = await noMatchBrowser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch));

        foreach (HttpResponseMessage response in new[] { match, noMatch })
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Null(response.Headers.RetryAfter);
            Assert.False(response.Headers.Contains("Retry-After"));
        }

        JsonObject matchBody = await BodyWithoutTraceIdAsync(match);
        Assert.Equal(matchBody.ToJsonString(), (await BodyWithoutTraceIdAsync(noMatch)).ToJsonString());
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/rate-limited.md", (string?)matchBody["type"]);
        Assert.Equal(429, (int?)matchBody["status"]);
        foreach (string login in new[] { matchLogin, noMatchLogin })
        {
            List<AuditLogEntry> entries = await factory.LinkingAuditsAsync(login);
            Assert.Equal(
                [(AuditAction.LinkingStart, AuditResult.Succeeded, null, StoredClientIp), (AuditAction.LinkingRateLimited, AuditResult.Rejected, null, StoredClientIp)],
                entries.Select(entry => (entry.Action, entry.Result, entry.CustomerId, entry.ClientIp)));
            LinkingChallenge blank = Assert.Single(await factory.ChallengesAsync(login));
            Assert.Equal((null, null, null, null, null), (blank.SupersededAtUtc, blank.ConsumedAtUtc, blank.CustomerId, blank.OtpHmac, blank.OtpSentAtUtc));
        }

        // The queued fifth start runs once a worker is free; neither refused start ever reaches core banking.
        Array.ForEach(holds, hold => hold.Release());
        await factory.WaitForStopsAsync(FillerLogins, [AuditAction.LinkingNoMatch], count: 5);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(5, ports.CoreBanking.Calls);
        Assert.Equal(2, (await factory.LinkingAuditsAsync(matchLogin)).Count);
    }

    [Fact]
    public async Task Start_QueueFullAndTheRateLimitedAuditSaveFails_StillThe429AndLogsOnlyTheChallengeAndAction()
    {
        var ports = new LinkingFakes.Ports();
        var failAudit = new FailAuditInsertInterceptor { OnlyAction = nameof(AuditAction.LinkingRateLimited) };
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await StartApiWithAQueueOfOneAsync(ports, services =>
        {
            telemetry.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit));
        });
        LinkingFakes.Hold[] holds = await FillTheQueueAsync(factory, ports);
        string login = LinkingApi.NewLogin("full-audit-fails");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        HttpResponseMessage response = await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Null(response.Headers.RetryAfter);
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/rate-limited.md", (string?)(await BodyWithoutTraceIdAsync(response))["type"]);
        Assert.Equal(1, failAudit.Failed);
        Assert.Equal([(AuditAction.LinkingStart, AuditResult.Succeeded)], (await factory.LinkingAuditsAsync(login)).Select(entry => (entry.Action, entry.Result)));
        Guid challengeId = Assert.Single(await factory.ChallengesAsync(login)).Id;
        LogRecord error = Assert.Single(telemetry.Logs, log => log.LogLevel == LogLevel.Error && log.CategoryName == "DisputeDesk.Application.Linking.StartLinking.StartLinkingHandler");
        Assert.Null(error.Exception);
        Assert.Equal(
            [("Action", "LinkingRateLimited"), ("ChallengeId", challengeId.ToString())],
            (error.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").Select(attribute => (attribute.Key, attribute.Value?.ToString())).Order());
        Array.ForEach(holds, hold => hold.Release());
    }

    private async Task<ApiFactory> StartApiWithAQueueOfOneAsync(LinkingFakes.Ports ports, Action<IServiceCollection>? configureServices) =>
        await LinkingApi.StartApiAsync(
            postgres, ports, new TestClock(), configureServices, new Dictionary<string, string?> { ["Linking:QueueCapacity"] = "1" });

    /// <summary>
    /// Four filler starts take the four workers, held at core banking, and a fifth waits in the queue of one, so the
    /// queue is full. Returns the holds, for the test to release.
    /// </summary>
    internal static async Task<LinkingFakes.Hold[]> FillTheQueueAsync(ApiFactory factory, LinkingFakes.Ports ports)
    {
        LinkingFakes.Hold[] holds = [.. Enumerable.Range(0, 4).Select(_ => ports.CoreBanking.HoldNext())];
        foreach (string login in FillerLogins.Take(4))
        {
            using HttpClient browser = await factory.SignInUnlinkedAsync(login);
            await browser.StartAcceptedAsync(LinkingFakes.NoMatch);
        }

        await Task.WhenAll(holds.Select(hold => hold.Reached));
        using HttpClient queued = await factory.SignInUnlinkedAsync(FillerLogins[4]);
        await queued.StartAcceptedAsync(LinkingFakes.NoMatch);
        return holds;
    }

    private static async Task<JsonObject> BodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        JsonObject body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))?.AsObject()
            ?? throw new JsonException("The problem body is not a JSON object.");
        Assert.True(body.Remove("traceId"));
        return body;
    }
}
