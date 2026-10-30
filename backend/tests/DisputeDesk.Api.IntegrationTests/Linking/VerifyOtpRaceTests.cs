using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.2 AC1, AC6 and AC7: the conditional consume lets one verification through, so parallel verifications count at
/// most three attempts and link once, a later start that supersedes the challenge mid-verification links nothing, and
/// two logins racing for one customer link one and answer the other as a counted wrong code (AD-15), never a 500. A
/// verification that loses the consume does not count a lock.
/// </summary>
public class VerifyOtpRaceTests(PostgresFixture postgres)
{
    /// <summary>The consume's <c>ExecuteUpdate</c>; the attempt statement sets <c>Attempts</c> instead.</summary>
    private const string ConsumeUpdate = "SET \"ConsumedAtUtc\"";

    [Fact]
    public async Task Verify_TenCorrectCodesInParallel_LinksOnceAndCountsExactlyThreeAttempts()
    {
        var ports = new LinkingFakes.Ports();
        var consume = new GateInterceptor(ConsumeUpdate);
        await using ApiFactory factory = await StartGatedApiAsync(ports, consume);
        var events = new ConcurrentQueue<string>();
        using MeterListener listener = factory.ListenToLinkingEvents(events);
        string login = LinkingApi.NewLogin("parallel");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);

        Task<HttpResponseMessage[]> verifying = Task.WhenAll(Enumerable.Range(0, 10).Select(_ => browser.VerifyCodeAsync(otp)));
        // Every consume waits until the three counted attempts reach it, so all three count before any consumes.
        await consume.WaitForHeldAsync(3);
        Assert.Equal(3, (await factory.CurrentChallengeAsync(login))?.Attempts);
        consume.Open();
        HttpResponseMessage[] responses = await verifying;

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        foreach (HttpResponseMessage response in responses.Where(response => response.StatusCode != HttpStatusCode.NoContent))
        {
            string expected = response.StatusCode switch
            {
                HttpStatusCode.UnprocessableEntity => LinkingProblems.OtpIncorrect,
                HttpStatusCode.Conflict => LinkingProblems.LinkingLocked,
                HttpStatusCode.Forbidden => LinkingProblems.AlreadyLinked,
                _ => throw new Xunit.Sdk.XunitException($"Unexpected {response.StatusCode}."),
            };
            Assert.Equal(ProblemType.For(expected), await response.ProblemTypeAsync());
        }

        CustomerLink link = Assert.Single(await factory.LinksAsync());
        Assert.Equal((login, LinkingFakes.D.CustomerId), (link.LoginSub, link.CustomerId));
        LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Equal(3, challenge.Attempts);
        Assert.NotNull(challenge.ConsumedAtUtc);
        List<AuditLogEntry> audits = await factory.VerifyAuditsAsync(login);
        Assert.Single(audits, entry => entry is { Action: AuditAction.Linked, Result: AuditResult.Succeeded });
        Assert.Equal(2, audits.Count(entry => entry is { Action: AuditAction.Linked, Result: AuditResult.Rejected }));
        // The two that lost the consume did not lock the challenge: it linked.
        Assert.Equal(["event=completed"], events.Where(tags => tags != "event=started"));
    }

    [Fact]
    public async Task Verify_CorrectCodeWhileALaterStartSupersedesTheChallenge_AnswersAsAWrongCodeAndLinksNothing()
    {
        var ports = new LinkingFakes.Ports();
        var consume = new GateInterceptor(ConsumeUpdate);
        await using ApiFactory factory = await StartGatedApiAsync(ports, consume);
        string login = LinkingApi.NewLogin("superseded");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);

        Task<HttpResponseMessage> verifying = browser.VerifyCodeAsync(otp);
        await consume.WaitForHeldAsync(1);
        await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        consume.Open();
        HttpResponseMessage response = await verifying;

        await response.AssertOtpIncorrectAsync(attemptsRemaining: 2);
        Assert.Empty(await factory.LinksAsync());
        AuditLogEntry rejected = Assert.Single(await factory.VerifyAuditsAsync(login));
        Assert.Equal((AuditAction.Linked, AuditResult.Rejected, LinkingFakes.D.CustomerId), (rejected.Action, rejected.Result, rejected.CustomerId));
    }

    [Fact]
    public async Task Verify_ThirdAttemptLosingTheConsumeToALaterStart_AnswersLockedWithoutCountingALock()
    {
        var ports = new LinkingFakes.Ports();
        var consume = new GateInterceptor(ConsumeUpdate);
        await using ApiFactory factory = await StartGatedApiAsync(ports, consume);
        var events = new ConcurrentQueue<string>();
        using MeterListener listener = factory.ListenToLinkingEvents(events);
        string login = LinkingApi.NewLogin("superseded-third");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        await (await browser.VerifyCodeAsync(VerifyApi.WrongCode(otp))).AssertOtpIncorrectAsync(attemptsRemaining: 2);
        await (await browser.VerifyCodeAsync(VerifyApi.WrongCode(otp))).AssertOtpIncorrectAsync(attemptsRemaining: 1);

        Task<HttpResponseMessage> verifying = browser.VerifyCodeAsync(otp);
        await consume.WaitForHeldAsync(1);
        await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        consume.Open();
        HttpResponseMessage response = await verifying;

        await response.AssertLockedAsync();
        Assert.Empty(await factory.LinksAsync());
        Assert.Equal(
            [AuditAction.LinkingOtpIncorrect, AuditAction.LinkingOtpIncorrect, AuditAction.Linked],
            (await factory.VerifyAuditsAsync(login)).Select(entry => entry.Action).Order());
        // The superseded challenge was taken by the start, not locked.
        Assert.DoesNotContain(events, tags => tags != "event=started");
    }

    [Fact]
    public async Task Verify_TwoLoginsRacingForOneCustomer_LinksOneAndAnswersTheOtherAsACountedWrongCode()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string[] logins = [LinkingApi.NewLogin("racer-1"), LinkingApi.NewLogin("racer-2")];
        HttpClient[] browsers = [await factory.SignInUnlinkedAsync(logins[0]), await factory.SignInUnlinkedAsync(logins[1])];
        // Both OTPs are sent before either verifies, so both jobs found customer D unlinked.
        string[] otps = [
            await factory.StartAndReadOtpAsync(browsers[0], ports, logins[0], LinkingFakes.D),
            await factory.StartAndReadOtpAsync(browsers[1], ports, logins[1], LinkingFakes.D),
        ];

        HttpResponseMessage[] responses = await Task.WhenAll(browsers[0].VerifyCodeAsync(otps[0]), browsers[1].VerifyCodeAsync(otps[1]));

        int winner = Array.FindIndex(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.NotEqual(-1, winner);
        int loser = 1 - winner;
        await responses[loser].AssertOtpIncorrectAsync(attemptsRemaining: 2);
        CustomerLink link = Assert.Single(await factory.LinksAsync());
        Assert.Equal((logins[winner], LinkingFakes.D.CustomerId), (link.LoginSub, link.CustomerId));
        // The loser's consume rolled back with its failed insert.
        LinkingChallenge loserChallenge = await factory.CurrentChallengeAsync(logins[loser]) ?? throw new InvalidOperationException("The loser's challenge is not current.");
        Assert.Equal(1, loserChallenge.Attempts);
        AuditLogEntry rejected = Assert.Single(await factory.VerifyAuditsAsync(logins[loser]));
        Assert.Equal((AuditAction.Linked, AuditResult.Rejected, LinkingFakes.D.CustomerId), (rejected.Action, rejected.Result, rejected.CustomerId));
        Array.ForEach(browsers, browser => browser.Dispose());
    }

    /// <summary>
    /// The exception to AC7 (decided 2026-10-30): when the losing login's correct code is its third attempt, the lost
    /// insert locks its challenge, so it answers and audits <c>LinkingLocked</c> and counts <c>locked</c> once (AC8).
    /// </summary>
    [Fact]
    public async Task Verify_LoserOfTheCustomerOnItsThirdAttempt_AnswersLockedAndCountsALockOnce()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        var events = new ConcurrentQueue<string>();
        using MeterListener listener = factory.ListenToLinkingEvents(events);
        string loginA = LinkingApi.NewLogin("clash-a");
        string loginB = LinkingApi.NewLogin("clash-b");
        using HttpClient browserA = await factory.SignInUnlinkedAsync(loginA);
        using HttpClient browserB = await factory.SignInUnlinkedAsync(loginB);
        // Both OTPs are sent before either verifies, so both jobs found customer D unlinked.
        string otpA = await factory.StartAndReadOtpAsync(browserA, ports, loginA, LinkingFakes.D);
        string otpB = await factory.StartAndReadOtpAsync(browserB, ports, loginB, LinkingFakes.D);
        await (await browserB.VerifyCodeAsync(VerifyApi.WrongCode(otpB))).AssertOtpIncorrectAsync(attemptsRemaining: 2);
        await (await browserB.VerifyCodeAsync(VerifyApi.WrongCode(otpB))).AssertOtpIncorrectAsync(attemptsRemaining: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await browserA.VerifyCodeAsync(otpA)).StatusCode);

        HttpResponseMessage response = await browserB.VerifyCodeAsync(otpB);

        await response.AssertLockedAsync();
        CustomerLink link = Assert.Single(await factory.LinksAsync());
        Assert.Equal((loginA, LinkingFakes.D.CustomerId), (link.LoginSub, link.CustomerId));
        // The consume rolled back with the failed insert: the challenge is current, locked and not consumed.
        LinkingChallenge challengeB = await factory.CurrentChallengeAsync(loginB) ?? throw new InvalidOperationException("B's challenge is not current.");
        Assert.Equal(3, challengeB.Attempts);
        Assert.Null(challengeB.ConsumedAtUtc);
        List<AuditLogEntry> audits = await factory.VerifyAuditsAsync(loginB);
        Assert.Equal(
            [AuditAction.LinkingOtpIncorrect, AuditAction.LinkingOtpIncorrect, AuditAction.LinkingLocked],
            audits.Select(entry => entry.Action).Order());
        AuditLogEntry locked = Assert.Single(audits, entry => entry.Action == AuditAction.LinkingLocked);
        Assert.Equal((AuditResult.Rejected, LinkingFakes.D.CustomerId), (locked.Result, locked.CustomerId));
        Assert.Equal(["event=completed", "event=locked"], events.Where(tags => tags != "event=started"));
    }

    private Task<ApiFactory> StartGatedApiAsync(LinkingFakes.Ports ports, GateInterceptor consume) =>
        LinkingApi.StartApiAsync(
            postgres, ports, new TestClock(), services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(consume)));
}
