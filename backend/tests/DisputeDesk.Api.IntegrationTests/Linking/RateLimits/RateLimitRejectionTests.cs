using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace DisputeDesk.Api.IntegrationTests.Linking.RateLimits;

/// <summary>
/// 6.3 AC2 and AC3: either limiter answers the same 429 <c>rate-limited</c> problem with <c>Retry-After</c> the window
/// length; the first rejection per login or IP in a window is audited, and every rejection is counted by the built-in
/// <c>aspnetcore.rate_limiting.requests</c> metric.
/// </summary>
public class RateLimitRejectionTests(PostgresFixture postgres)
{
    private const string RateLimitedDetail = "Too many linking requests. Try again later.";

    [Fact]
    public async Task Rejected_ByEitherLimiter_GetsTheSameRateLimitedProblemWithRetryAfterTheWindowLength()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        string login = LinkingApi.NewLogin("body-login");
        using HttpClient byLogin = await api.Factory.SignInUnlinkedAsync(login);
        await SpendAsync(byLogin, 5);
        HttpResponseMessage loginRejection = await RateLimitApi.MalformedVerifyAsync(byLogin);
        // The login rejection spent two IP permits (LinkingRateLimitTests): 7 spent, 13 more fill the IP.
        await SpendIpAsync(api, 13);
        string ipLogin = LinkingApi.NewLogin("body-ip");
        using HttpClient byIp = await api.Factory.SignInUnlinkedAsync(ipLogin);
        HttpResponseMessage ipRejection = await byIp.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D));

        foreach (HttpResponseMessage response in new[] { loginRejection, ipRejection })
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(["60"], response.Headers.GetValues("Retry-After"));
            JsonObject body = await BodyAsync(response);
            Assert.Equal(["detail", "status", "title", "traceId", "type"], body.Select(field => field.Key).Order());
            Assert.Equal(ProblemType.For(CommonProblems.RateLimited), (string?)body["type"]);
            Assert.Equal(429, (int?)body["status"]);
            Assert.Equal("Too Many Requests", (string?)body["title"]);
            // A fixed sentence: never the login, the address or a count.
            Assert.Equal(RateLimitedDetail, (string?)body["detail"]);
            Assert.Matches("^[0-9a-f]{32}$", (string?)body["traceId"]);
        }

        Assert.Equal(await loginRejection.StatusAndBodyAsync(), await ipRejection.StatusAndBodyAsync());
    }

    [Fact]
    public async Task Rejected_ManyTimes_AuditsTheFirstPerLoginOrIpInTheWindowAndCountsEveryOneInTheExportedMetric()
    {
        var telemetry = new TelemetryCapture();
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres, configureServices: telemetry.AddTo);
        var rejections = new ConcurrentQueue<string>();
        using MeterListener listener = ListenToRejections(api.Factory, rejections);

        // The login limit rejects the first login three times: one audit entry.
        string first = LinkingApi.NewLogin("audit-first");
        using HttpClient firstBrowser = await api.Factory.SignInUnlinkedAsync(first);
        await SpendAsync(firstBrowser, 5);
        await RejectAsync(firstBrowser, 3);
        // Each login rejection spent two IP permits (LinkingRateLimitTests): 11 are spent, 9 more fill the IP. The IP
        // limit then rejects a second login twice and a third once.
        await SpendIpAsync(api, 9);
        string second = LinkingApi.NewLogin("audit-second");
        using HttpClient secondBrowser = await api.Factory.SignInUnlinkedAsync(second);
        await RejectAsync(secondBrowser, 2);
        string third = LinkingApi.NewLogin("audit-third");
        using HttpClient thirdBrowser = await api.Factory.SignInUnlinkedAsync(third);
        await RejectAsync(thirdBrowser, 1);

        foreach (string login in new[] { first, second, third })
        {
            AuditLogEntry entry = Assert.Single(await api.Factory.LinkingAuditsAsync(login), entry => entry.Action == AuditAction.LinkingRateLimited);
            Assert.Equal(
                (AuditResult.Rejected, Domain.Disputes.ActorType.Customer, login, (string?)null, IPAddress.Parse("203.0.113.7")),
                (entry.Result, entry.ActorType, entry.ActorId, entry.CustomerId, entry.ClientIp));
        }

        Assert.Equal(6, rejections.Count(tags => tags.Contains("aspnetcore.rate_limiting.result=global_limiter", StringComparison.Ordinal)));
        Metric exported = await telemetry.WaitForMetricAsync(api.Factory.Services, "aspnetcore.rate_limiting.requests");
        Assert.Equal(MetricType.LongSum, exported.MetricType);
    }

    [Fact]
    public async Task Rejected_TheSameLoginFromANewIp_AuditsAgain()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres);
        string login = LinkingApi.NewLogin("audit-new-ip");
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(login);
        await SpendAsync(browser, 5);
        await RejectAsync(browser, 1);

        // The login limit rejects it again; its login partition was already rejected, but this IP partition was not.
        api.ClientAddress = IPAddress.Parse("198.51.100.30");
        await RejectAsync(browser, 1);

        Assert.Equal(
            ["198.51.100.30", "203.0.113.7"],
            (await api.Factory.LinkingAuditsAsync(login))
                .Where(entry => entry.Action == AuditAction.LinkingRateLimited)
                .Select(entry => entry.ClientIp?.ToString())
                .Order());
    }

    [Fact]
    public async Task Rejected_AndTheAuditSaveFails_StillThe429AndLogsOnlyTheAction()
    {
        var failAudit = new FailAuditInsertInterceptor { OnlyAction = nameof(AuditAction.LinkingRateLimited) };
        var telemetry = new TelemetryCapture();
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres, configureServices: services =>
        {
            telemetry.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit));
        });
        string login = LinkingApi.NewLogin("audit-fails");
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(login);
        await SpendAsync(browser, 5);

        HttpResponseMessage response = await RateLimitApi.MalformedVerifyAsync(browser);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(RateLimitedDetail, (string?)(await BodyAsync(response))["detail"]);
        Assert.Equal(1, failAudit.Failed);
        Assert.DoesNotContain(await api.Factory.LinkingAuditsAsync(login), entry => entry.Action == AuditAction.LinkingRateLimited);
        LogRecord error = Assert.Single(telemetry.Logs, log => log.LogLevel == LogLevel.Error && log.CategoryName == "DisputeDesk.Api.Linking.RateLimiting.LinkingRateLimits");
        Assert.Null(error.Exception);
        Assert.Equal(
            [("Action", "LinkingRateLimited")],
            (error.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").Select(attribute => (attribute.Key, attribute.Value?.ToString())));
    }

    /// <summary>Sends <paramref name="count"/> malformed verifies, each of which must pass the limits (400).</summary>
    private static async Task SpendAsync(HttpClient browser, int count)
    {
        for (int request = 0; request < count; request++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        }
    }

    private static async Task RejectAsync(HttpClient browser, int count)
    {
        for (int request = 0; request < count; request++)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        }
    }

    /// <summary>Spends <paramref name="count"/> more IP permits, from new logins of at most five requests each.</summary>
    private static async Task SpendIpAsync(RateLimitApi api, int count)
    {
        while (count > 0)
        {
            using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("spend-ip"));
            int requests = Math.Min(count, 5);
            await SpendAsync(browser, requests);
            count -= requests;
        }
    }

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))?.AsObject()
            ?? throw new JsonException("The problem body is not a JSON object.");

    /// <summary>Puts the tags of every <c>aspnetcore.rate_limiting.requests</c> measurement of this test's Api in <paramref name="measurements"/>.</summary>
    private static MeterListener ListenToRejections(ApiFactory factory, ConcurrentQueue<string> measurements)
    {
        IMeterFactory meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        var listener = new MeterListener
        {
            // Only this test's Api: its own meter factory is the meter's scope.
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Name == "aspnetcore.rate_limiting.requests")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            measurements.Enqueue(string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"))));
        listener.Start();
        return listener;
    }
}
