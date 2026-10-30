using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC5: one conditional update claims the send, the OTP goes to the cellphone on record and only its HMAC is stored;
/// a failed or cancelled send releases the claim (keeping its instant, 6.5), is audited as failed and is not retried; the
/// key is checked at start-up.
/// </summary>
public class LinkingOtpSendTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Job_ChecksPass_ClaimsWithTheClockSendsTheOtpAndStoresOnlyItsHmac()
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        string login = LinkingApi.NewLogin("sent");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(LinkingFakes.D);

        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        Assert.Equal(LinkingFakes.D.CustomerId, entries[^1].CustomerId);
        (string cellphone, string message) = Assert.Single(ports.Sms.Received);
        Assert.Equal(LinkingFakes.D.Cellphone, cellphone);
        string otp = ports.Sms.OnlyOtp();
        Assert.Matches("^[0-9]{6}$", otp);
        Assert.Contains(otp, message, StringComparison.Ordinal);
        LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Equal(LinkingFakes.D.CustomerId, challenge.CustomerId);
        Assert.Equal(clock.NowAsStored, challenge.OtpSentAtUtc);
        Assert.Equal(clock.NowAsStored.AddMinutes(5), challenge.ExpiresAtUtc);
        byte[] expected = HMACSHA256.HashData(Convert.FromBase64String(ApiFactory.TestOtpHmacKey), Encoding.UTF8.GetBytes(challenge.Id.ToString("D") + otp));
        Assert.Equal(expected, challenge.OtpHmac);
        Assert.Equal(0, challenge.Attempts);
    }

    [Fact]
    public async Task Job_SendThrows_ReleasesTheClaimAuditsFailedMarksTheSpanAndDoesNotRetry()
    {
        var ports = new LinkingFakes.Ports();
        // As an SMTP refusal can: the message names the recipient.
        ports.Sms.Failure = new InvalidOperationException($"5.1.1 <{LinkingFakes.D.Cellphone}@sms.disputedesk.test>: Recipient address rejected");
        var telemetry = new TelemetryCapture();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock, telemetry.AddTo);
        string login = LinkingApi.NewLogin("send-fails");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        var requestTrace = ActivityTraceId.CreateRandom();

        HttpResponseMessage response = await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D), configure: request => request.Headers.Add("traceparent", TraceParent(requestTrace)));

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Failed);
        Assert.Equal(LinkingFakes.D.CustomerId, entries[^1].CustomerId);
        // 6.5: the release clears the OTP and its lifetime but keeps the claim's instant, which still counts toward the cap.
        LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Equal((null, null, clock.NowAsStored), (challenge.OtpHmac, challenge.ExpiresAtUtc, challenge.OtpSentAtUtc));
        Assert.Single(ports.Sms.Received);
        Activity span = await telemetry.WaitForSpanAsync(span => span.Links.Any(link => link.Context.TraceId == requestTrace));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("The OTP could not be sent.", span.StatusDescription);
        Assert.Empty(span.Events);
        Assert.DoesNotContain(telemetry.Logs, log => log.Exception is not null || (log.FormattedMessage ?? "").Contains(LinkingFakes.D.Cellphone, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Job_SendCancelledByShutdown_ReleasesTheClaimAndAuditsFailed()
    {
        // The host stops while the SMS is being handed over: the send is cancelled, and the release and the audit still
        // run to completion, after the stopping token has fired.
        var ports = new LinkingFakes.Ports();
        LinkingFakes.Hold send = ports.Sms.HoldNext();
        string database = await postgres.CreateMigratedDatabaseAsync();
        string login = LinkingApi.NewLogin("send-cancelled");
        var clock = new TestClock();
        var factory = new ApiFactory(database, configureServices: ports.AddTo, clock: clock);
        await using (factory)
        {
            using HttpClient browser = await factory.SignInUnlinkedAsync(login);
            await browser.StartAcceptedAsync(LinkingFakes.D);
            await send.Reached;
        }

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options);
        LinkingChallenge challenge = await db.Set<LinkingChallenge>().SingleAsync(row => row.LoginSub == login, TestContext.Current.CancellationToken);
        Assert.Equal((null, null, clock.NowAsStored), (challenge.OtpHmac, challenge.ExpiresAtUtc, challenge.OtpSentAtUtc));
        AuditLogEntry last = await db.Set<AuditLogEntry>().Where(entry => entry.ActorId == login && entry.Action == AuditAction.LinkingOtpSent).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AuditResult.Failed, last.Result);
    }

    [Fact]
    public async Task Job_SendLongerThanTheBudget_StillCompletesBecauseTheBudgetEndsAtTheClaim()
    {
        var ports = new LinkingFakes.Ports();
        LinkingFakes.Hold send = ports.Sms.HoldNext();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(
            postgres, ports, new TestClock(), settings: new Dictionary<string, string?> { ["Linking:Budget"] = "00:00:01" });
        string login = LinkingApi.NewLogin("slow-send");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(LinkingFakes.D);
        await send.Reached;
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        send.Release();

        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        Assert.NotNull(Assert.Single(await factory.ChallengesAsync(login)).OtpSentAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("BwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBw==")] // 31 bytes: one short of RFC 2104's minimum for SHA-256
    public void ApiStart_InvalidOtpHmacKey_StopsTheApiWithAnErrorNamingTheKeyButNotItsValue(string? key)
    {
        using var factory = new ApiFactory(connectionString: null, settings: new Dictionary<string, string?> { ["Linking:OtpHmacKey"] = key });

        Exception refused = Assert.ThrowsAny<Exception>(() => factory.Services);

        string error = refused.ToString();
        Assert.Contains("Linking:OtpHmacKey must be base64 for at least 32 bytes.", error, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(key))
        {
            Assert.DoesNotContain(key, error, StringComparison.Ordinal);
        }
    }

    private static string TraceParent(ActivityTraceId traceId) => $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
}
