using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.2 AC3: three attempts, then locked. Wrong codes answer <c>attemptsRemaining</c> 2 and 1, a wrong third locks, a
/// correct third links, and a fourth attempt is locked whatever its code. An expired challenge counts and answers the same.
/// </summary>
public class VerifyOtpAttemptTests(PostgresFixture postgres)
{
    private static readonly System.Net.IPAddress StoredClientIp = System.Net.IPAddress.Parse("203.0.113.7");

    [Fact]
    public async Task Verify_ThreeWrongCodesThenTheCorrectOne_Answers2Then1ThenLockedTwiceAndLinksNothing()
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        string login = LinkingApi.NewLogin("locks");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        string wrong = VerifyApi.WrongCode(otp);

        await (await browser.VerifyCodeAsync(wrong)).AssertOtpIncorrectAsync(attemptsRemaining: 2);
        await (await browser.VerifyCodeAsync(wrong)).AssertOtpIncorrectAsync(attemptsRemaining: 1);
        await (await browser.VerifyCodeAsync(wrong)).AssertLockedAsync();
        await (await browser.VerifyCodeAsync(otp)).AssertLockedAsync();

        Assert.Empty(await factory.LinksAsync());
        Assert.Equal(3, (await factory.CurrentChallengeAsync(login))?.Attempts);
        List<AuditLogEntry> audits = await factory.VerifyAuditsAsync(login);
        Assert.Equal(
            [AuditAction.LinkingOtpIncorrect, AuditAction.LinkingOtpIncorrect, AuditAction.LinkingLocked, AuditAction.LinkingLocked],
            audits.Select(entry => entry.Action));
        Assert.All(audits, entry =>
        {
            Assert.Equal(AuditResult.Rejected, entry.Result);
            Assert.Equal((ActorType.Customer, login, LinkingFakes.D.CustomerId), (entry.ActorType, entry.ActorId, entry.CustomerId));
            Assert.Equal(StoredClientIp, entry.ClientIp);
            Assert.Equal(clock.NowAsStored, entry.OccurredAtUtc);
        });
    }

    [Fact]
    public async Task Verify_TwoWrongCodesThenTheCorrectOne_LinksOnTheThirdAttempt()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("third-links");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);

        await (await browser.VerifyCodeAsync(VerifyApi.WrongCode(otp))).AssertOtpIncorrectAsync(attemptsRemaining: 2);
        await (await browser.VerifyCodeAsync(VerifyApi.WrongCode(otp))).AssertOtpIncorrectAsync(attemptsRemaining: 1);
        HttpResponseMessage third = await browser.VerifyCodeAsync(otp);

        Assert.Equal(HttpStatusCode.NoContent, third.StatusCode);
        Assert.Equal(login, Assert.Single(await factory.LinksAsync()).LoginSub);
        Assert.Equal(
            [AuditAction.LinkingOtpIncorrect, AuditAction.LinkingOtpIncorrect, AuditAction.Linked],
            (await factory.VerifyAuditsAsync(login)).Select(entry => entry.Action));
    }

    [Fact]
    public async Task Verify_ExpiredChallengeWithTheCorrectCode_CountsEachAttemptAndLocksOnTheThird()
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        string login = LinkingApi.NewLogin("expired");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        clock.Advance(TimeSpan.FromMinutes(6));

        await (await browser.VerifyCodeAsync(otp)).AssertOtpIncorrectAsync(attemptsRemaining: 2);
        await (await browser.VerifyCodeAsync(otp)).AssertOtpIncorrectAsync(attemptsRemaining: 1);
        await (await browser.VerifyCodeAsync(otp)).AssertLockedAsync();

        Assert.Empty(await factory.LinksAsync());
        Assert.Equal(
            [AuditAction.LinkingOtpExpired, AuditAction.LinkingOtpExpired, AuditAction.LinkingLocked],
            (await factory.VerifyAuditsAsync(login)).Select(entry => entry.Action));
    }
}
