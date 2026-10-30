using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC1: every well-formed start by an unlinked login gets the same 202 and does the same writes, whatever the
/// details; the request calls no port; parallel starts leave one current challenge. A full queue is 6.5's
/// <see cref="StartLinkingQueueFullTests"/>.
/// </summary>
public class StartLinkingUniformTests(PostgresFixture postgres)
{
    // Headers that differ between any two responses: the date, and the trace headers when a server sends them.
    private static readonly string[] VaryingHeaders = ["Date", "traceparent", "tracestate", "Request-Id"];

    [Fact]
    public async Task Start_MatchUnlinkedNoMatchAndMatchLinked_SameResponseAndSameWritesWithNoPortCalled()
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        await factory.LinkAsync(LinkingApi.NewLogin("customer-a"), LinkingFakes.A.CustomerId);
        // Core banking holds every job's first step, so the jobs write nothing yet and a request that called it would hang.
        LinkingFakes.Hold[] holds = [ports.CoreBanking.HoldNext(), ports.CoreBanking.HoldNext(), ports.CoreBanking.HoldNext()];
        (string Login, LinkingFakes.Customer Details)[] starts =
            [(LinkingApi.NewLogin("for-d"), LinkingFakes.D), (LinkingApi.NewLogin("for-none"), LinkingFakes.NoMatch), (LinkingApi.NewLogin("for-a"), LinkingFakes.A)];

        var responses = new List<HttpResponseMessage>();
        foreach ((string login, LinkingFakes.Customer details) in starts)
        {
            HttpClient browser = await factory.SignInUnlinkedAsync(login);
            responses.Add(await browser.StartLinkingAsync(LinkingApi.Body(details)));
        }

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        List<string> first = await ComparableAsync(responses[0]);
        Assert.Equal(first, await ComparableAsync(responses[1]));
        Assert.Equal(first, await ComparableAsync(responses[2]));
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(ports.SimSwap.Cellphones);
        Assert.Empty(ports.Sms.Received);

        foreach ((string login, _) in starts)
        {
            LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
            Assert.Equal(clock.NowAsStored, challenge.CreatedAtUtc);
            Assert.Equal((null, null, 0, null, null, null, null), (challenge.CustomerId, challenge.OtpHmac, challenge.Attempts, challenge.ExpiresAtUtc, challenge.OtpSentAtUtc, challenge.SupersededAtUtc, challenge.ConsumedAtUtc));
            AuditLogEntry started = Assert.Single(await factory.LinkingAuditsAsync(login));
            Assert.Equal((AuditAction.LinkingStart, AuditResult.Succeeded, null), (started.Action, started.Result, started.CustomerId));
        }

        Array.ForEach(holds, hold => hold.Release());
    }

    [Fact]
    public async Task Start_WithACurrentChallenge_SupersedesItAndInsertsANewCurrentOneInOneSave()
    {
        // The update and the insert go in one SaveChanges under the filtered unique index: were the insert sent first,
        // the index would refuse it while the earlier challenge is still current.
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        LinkingFakes.Hold[] holds = [ports.CoreBanking.HoldNext(), ports.CoreBanking.HoldNext()];
        string login = LinkingApi.NewLogin("restart");
        HttpClient browser = await factory.SignInUnlinkedAsync(login);
        await browser.StartAcceptedAsync(LinkingFakes.D);
        clock.Advance(TimeSpan.FromSeconds(30));

        await browser.StartAcceptedAsync(LinkingFakes.D);

        List<LinkingChallenge> challenges = await factory.ChallengesAsync(login);
        Assert.Equal(2, challenges.Count);
        Assert.Equal(clock.NowAsStored, challenges[0].SupersededAtUtc);
        Assert.Null(challenges[1].SupersededAtUtc);
        Assert.Equal(clock.NowAsStored, challenges[1].CreatedAtUtc);
        Assert.Equal(2, (await factory.LinkingAuditsAsync(login)).Count(entry => entry.Action == AuditAction.LinkingStart && entry.Result == AuditResult.Succeeded));
        Array.ForEach(holds, hold => hold.Release());
    }

    [Fact]
    public async Task Start_TwoAtOnceByOneLogin_Both202AndOneCurrentChallengeOneAuditOneJob()
    {
        // Both starts read "no current challenge" before either saves, so only UQ_LinkingChallenges_LoginSub stops the second.
        var ports = new LinkingFakes.Ports();
        var rendezvous = new RendezvousInterceptor("FROM \"LinkingChallenges\"", parties: 2);
        await using ApiFactory factory = await LinkingApi.StartApiAsync(
            postgres, ports, new TestClock(), services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(rendezvous)));
        string login = LinkingApi.NewLogin("parallel");
        HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string token = await browser.AntiforgeryTokenAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(
            browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch), withAntiforgeryToken: false, request => request.Headers.Add("X-XSRF-TOKEN", token)),
            browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch), withAntiforgeryToken: false, request => request.Headers.Add("X-XSRF-TOKEN", token)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Single(await factory.ChallengesAsync(login));
        await factory.WaitForAuditAsync(login, AuditAction.LinkingNoMatch, AuditResult.Rejected);
        // A second job would have reached core banking alongside the first.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(1, ports.CoreBanking.Calls);
        List<AuditLogEntry> entries = await factory.LinkingAuditsAsync(login);
        Assert.Equal([(AuditAction.LinkingStart, AuditResult.Succeeded), (AuditAction.LinkingNoMatch, AuditResult.Rejected)], entries.Select(entry => (entry.Action, entry.Result)));
    }

    [Fact]
    public async Task Start_Accepted_CountsLinkingEventStartedOnTheDisputeDeskMeter()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        var measurements = new ConcurrentQueue<(string Instrument, long Value, string Tags)>();
        IMeterFactory meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        using var listener = new MeterListener
        {
            // Only this test's Api: its own meter factory is the meter's scope.
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name == "DisputeDesk")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}")))));
        listener.Start();
        HttpClient browser = await factory.SignInUnlinkedAsync(LinkingApi.NewLogin("metrics"));

        await browser.StartAcceptedAsync(LinkingFakes.NoMatch);
        await browser.StartAcceptedAsync(LinkingFakes.D);
        await browser.StartLinkingAsync("""{"idNumber":"1"}""");

        Assert.Equal(
            [("disputedesk.linking.events", 1L, "event=started"), ("disputedesk.linking.events", 1L, "event=started")],
            measurements.Where(measurement => measurement.Instrument == "disputedesk.linking.events"));
    }

    /// <summary>Status, every header but the varying ones (as name: value), and the body.</summary>
    private static async Task<List<string>> ComparableAsync(HttpResponseMessage response) =>
    [
        ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
        .. response.Headers.Concat(response.Content.Headers)
            .Where(header => !VaryingHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            .Select(header => $"{header.Key}: {string.Join(", ", header.Key == "Set-Cookie" ? header.Value.Select(CookieName) : header.Value)}")
            .Order(StringComparer.Ordinal),
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
    ];

    // The session cookie's value and expiry differ by session; its name and presence must not.
    private static string CookieName(string setCookie) => setCookie.Split('=')[0];
}
