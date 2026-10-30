using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC6: a start by the same login supersedes the earlier challenge. A job held before its claim then claims nothing
/// and sends nothing; a send already in flight completes, harmlessly, because only the current challenge can verify.
/// </summary>
public class LinkingRestartTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Restart_WhileTheEarlierJobIsBeforeItsClaim_EarlierJobClaimsNothingAndSendsNothing()
    {
        var ports = new LinkingFakes.Ports();
        // Held at the match, before the customer's lock (6.5): a job held inside the lock would make the second job for
        // the same customer wait for it.
        LinkingFakes.Hold firstCheck = ports.CoreBanking.HoldNext();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("restart-before-claim");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        await browser.StartAcceptedAsync(LinkingFakes.D);
        await firstCheck.Reached;

        await browser.StartAcceptedAsync(LinkingFakes.D);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        firstCheck.Release();

        List<Application.Audit.AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Rejected);
        Assert.Equal(LinkingFakes.D.CustomerId, Assert.Single(entries, entry => entry.Result == AuditResult.Rejected).CustomerId);
        Assert.All(entries, entry => Assert.Equal(System.Net.IPAddress.Parse("203.0.113.7"), entry.ClientIp));
        Assert.Single(ports.Sms.Received);
        List<LinkingChallenge> challenges = await factory.ChallengesAsync(login);
        LinkingChallenge superseded = Assert.Single(challenges, challenge => challenge.SupersededAtUtc is not null);
        Assert.Equal((null, null, null), (superseded.CustomerId, superseded.OtpHmac, superseded.OtpSentAtUtc));
        LinkingChallenge current = Assert.Single(challenges, challenge => challenge.SupersededAtUtc is null);
        Assert.NotNull(current.OtpSentAtUtc);
    }

    [Fact]
    public async Task Restart_WhileTheEarlierJobIsSending_SendCompletesAndTheNewChallengeStaysBlankUntilItsOwnClaim()
    {
        var ports = new LinkingFakes.Ports();
        LinkingFakes.Hold firstSend = ports.Sms.HoldNext();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("restart-during-send");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        await browser.StartAcceptedAsync(LinkingFakes.D);
        await firstSend.Reached;
        // The second job is held at its first step, so its challenge stays blank while the first send finishes.
        LinkingFakes.Hold secondMatch = ports.CoreBanking.HoldNext();

        await browser.StartAcceptedAsync(LinkingFakes.D);
        await secondMatch.Reached;
        firstSend.Release();

        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        List<LinkingChallenge> challenges = await factory.ChallengesAsync(login);
        LinkingChallenge earlier = Assert.Single(challenges, challenge => challenge.SupersededAtUtc is not null);
        Assert.NotNull(earlier.OtpSentAtUtc);
        LinkingChallenge current = Assert.Single(challenges, challenge => challenge.SupersededAtUtc is null);
        Assert.Equal((null, null, null), (current.CustomerId, current.OtpHmac, current.OtpSentAtUtc));

        secondMatch.Release();
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded, count: 2);
        LinkingChallenge claimed = Assert.Single(await factory.ChallengesAsync(login), challenge => challenge.SupersededAtUtc is null);
        Assert.Equal(current.Id, claimed.Id);
        Assert.NotNull(claimed.OtpSentAtUtc);
        Assert.Equal(2, ports.Sms.Received.Count);
    }
}
