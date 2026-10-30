using System.Diagnostics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC7: every stop is one audit entry through <c>AuditLog.Record</c>, with the login, the customer once matched, the
/// connection's address and, for the job's entries, the trace of the job's span, which links to the request.
/// </summary>
public class LinkingAuditTests(PostgresFixture postgres)
{
    private static readonly IPAddress StoredClientIp = IPAddress.Parse("203.0.113.7");

    public static TheoryData<string> Stops => ["no match", "already linked", "core banking unavailable", "budget passed in the SIM-swap check", "SIM swap", "sent", "send failed"];

    [Theory]
    [MemberData(nameof(Stops))]
    public async Task Job_EachStop_AuditsTheStartAndTheStopWithTheLoginTheCustomerOnceMatchedAndTheClientIp(string stop)
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        Dictionary<string, string?> settings = [];
        LinkingFakes.Customer details = LinkingFakes.D;
        (AuditAction Action, AuditResult Result, string? CustomerId) expected = (AuditAction.LinkingOtpSent, AuditResult.Succeeded, LinkingFakes.D.CustomerId);
        switch (stop)
        {
            case "no match":
                details = LinkingFakes.NoMatch;
                expected = (AuditAction.LinkingNoMatch, AuditResult.Rejected, null);
                break;
            case "already linked":
                details = LinkingFakes.A;
                expected = (AuditAction.LinkingAlreadyLinked, AuditResult.Rejected, LinkingFakes.A.CustomerId);
                break;
            case "core banking unavailable":
                ports.CoreBanking.Unavailable = true;
                expected = (AuditAction.LinkingStart, AuditResult.Failed, null);
                break;
            case "budget passed in the SIM-swap check":
                // Held and never released, so the budget runs out during the call.
                ports.SimSwap.HoldNext();
                settings["Linking:Budget"] = "00:00:01";
                expected = (AuditAction.LinkingStart, AuditResult.Failed, LinkingFakes.D.CustomerId);
                break;
            case "SIM swap":
                details = LinkingFakes.C;
                expected = (AuditAction.LinkingSimSwapFailed, AuditResult.Rejected, LinkingFakes.C.CustomerId);
                break;
            case "send failed":
                ports.Sms.Failure = new IOException("The SMTP server went away.");
                expected = (AuditAction.LinkingOtpSent, AuditResult.Failed, LinkingFakes.D.CustomerId);
                break;
        }

        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock, settings: settings);
        await factory.LinkAsync(LinkingApi.NewLogin("customer-a"), LinkingFakes.A.CustomerId);
        string login = LinkingApi.NewLogin("audited");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(details);

        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, expected.Action, expected.Result);

        Assert.Equal(
            [(AuditAction.LinkingStart, AuditResult.Succeeded, null), expected],
            entries.Select(entry => (entry.Action, entry.Result, entry.CustomerId)));
        Assert.All(entries, entry =>
        {
            Assert.Equal(clock.NowAsStored, entry.OccurredAtUtc);
            Assert.Equal((ActorType.Customer, login), (entry.ActorType, entry.ActorId));
            Assert.Equal(StoredClientIp, entry.ClientIp);
            Assert.Null(entry.DisputeId);
        });
    }

    [Fact]
    public async Task Job_Entries_CarryTheJobSpansTraceAndTheSpanLinksToTheRequest()
    {
        var ports = new LinkingFakes.Ports();
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock(), telemetry.AddTo);
        string login = LinkingApi.NewLogin("traced");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        var requestTrace = ActivityTraceId.CreateRandom();

        await browser.StartLinkingAsync(
            LinkingApi.Body(LinkingFakes.D),
            configure: request => request.Headers.Add("traceparent", $"00-{requestTrace.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01"));

        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        Activity job = await telemetry.WaitForSpanAsync(span => span.Links.Any(link => link.Context.TraceId == requestTrace));
        Assert.Equal("DisputeDesk", job.Source.Name);
        Assert.NotEqual(requestTrace, job.TraceId);
        Assert.Equal(requestTrace.ToHexString(), entries[0].TraceId);
        Assert.Equal(job.TraceId.ToHexString(), entries[1].TraceId);
    }

    [Fact]
    public async Task Start_ForgedForwardedForAndAPlainIpv4Connection_StoresOnlyTheConnectionAddress()
    {
        var ports = new LinkingFakes.Ports();
        var plainIpv4 = IPAddress.Parse("198.51.100.23");
        await using ApiFactory factory = await LinkingApi.StartApiAsync(
            postgres, ports, new TestClock(), services => services.AddTransient<IStartupFilter>(_ => new ClientAddressStartupFilter(plainIpv4)));
        string login = LinkingApi.NewLogin("forwarded");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch), configure: request => request.Headers.Add("X-Forwarded-For", "192.0.2.99"));

        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingNoMatch, AuditResult.Rejected);
        Assert.All(entries, entry => Assert.Equal(plainIpv4, entry.ClientIp));
    }

    [Fact]
    public async Task SignInAndSignOut_KeepClientIpNull()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        string login = LinkingApi.NewLogin("sign-in");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.LogoutAsync(await browser.AntiforgeryTokenAsync());

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        List<AuditLogEntry> entries = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>()
            .Where(entry => entry.ActorId == login)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([AuditAction.SignIn, AuditAction.SignOut], entries.Select(entry => entry.Action).Order());
        Assert.All(entries, entry => Assert.Null(entry.ClientIp));
    }

    [Fact]
    public async Task Job_AuditSaveFails_LogsOnlyTheChallengeAndActionMarksTheSpanAndTheNextJobRuns()
    {
        var ports = new LinkingFakes.Ports();
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock(), services =>
        {
            telemetry.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit));
        });
        string login = LinkingApi.NewLogin("audit-fails");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        // The request's own entry saves; the job, held until the interceptor is armed, then fails to save its entry.
        LinkingFakes.Hold match = ports.CoreBanking.HoldNext();
        var requestTrace = ActivityTraceId.CreateRandom();
        await browser.StartLinkingAsync(
            LinkingApi.Body(LinkingFakes.NoMatch),
            configure: request => request.Headers.Add("traceparent", $"00-{requestTrace.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01"));
        await match.Reached;
        failAudit.Armed = true;
        match.Release();

        Activity job = await telemetry.WaitForSpanAsync(span => span.Links.Any(link => link.Context.TraceId == requestTrace));
        failAudit.Armed = false;

        Assert.Equal(1, failAudit.Failed);
        Assert.Equal(ActivityStatusCode.Error, job.Status);
        Guid challengeId = Assert.Single(await factory.ChallengesAsync(login)).Id;
        LogRecord error = Assert.Single(telemetry.Logs, log => log.LogLevel == LogLevel.Error && log.CategoryName == "DisputeDesk.Infrastructure.Linking.LinkingJobSteps");
        Assert.Null(error.Exception);
        Assert.Equal(
            [("Action", "LinkingNoMatch"), ("ChallengeId", challengeId.ToString())],
            (error.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").Select(attribute => (attribute.Key, attribute.Value?.ToString())).Order());

        await browser.StartAcceptedAsync(LinkingFakes.NoMatch);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingNoMatch, AuditResult.Rejected);
    }
}
