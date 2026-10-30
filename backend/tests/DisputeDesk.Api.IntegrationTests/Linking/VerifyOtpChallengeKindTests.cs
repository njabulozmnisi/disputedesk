using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.2 AC4: a challenge with no OTP to match, whichever stop of the job left it blank or released, answers and counts
/// attempt by attempt exactly like customer D's challenge given wrong codes; a login with no challenge row gets a first
/// wrong code's answer every time and is never counted (OWASP Authentication Cheat Sheet, generic responses).
/// </summary>
public class VerifyOtpChallengeKindTests(PostgresFixture postgres)
{
    public static TheoryData<string> Kinds => ["no match", "already linked", "SIM swap failed", "released"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Verify_ChallengeWithNoOtpToMatch_AnswersAndLocksExactlyLikeWrongCodesOnARealOne(string kind)
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        List<string> expected = await WrongCodeAnswersAsync(factory, ports);
        string login = LinkingApi.NewLogin("kind");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        switch (kind)
        {
            case "no match":
                await StartAndWaitAsync(factory, browser, login, LinkingFakes.NoMatch, AuditAction.LinkingNoMatch, AuditResult.Rejected);
                break;
            case "already linked":
                await factory.LinkAsync(LinkingApi.NewLogin("customer-a"), LinkingFakes.A.CustomerId);
                await StartAndWaitAsync(factory, browser, login, LinkingFakes.A, AuditAction.LinkingAlreadyLinked, AuditResult.Rejected);
                break;
            case "SIM swap failed":
                await StartAndWaitAsync(factory, browser, login, LinkingFakes.C, AuditAction.LinkingSimSwapFailed, AuditResult.Rejected);
                break;
            case "released":
                ports.Sms.Failure = new IOException("The SMTP server went away.");
                await StartAndWaitAsync(factory, browser, login, LinkingFakes.D, AuditAction.LinkingOtpSent, AuditResult.Failed);
                break;
        }

        LinkingChallenge before = await factory.CurrentChallengeAsync(login) ?? throw new InvalidOperationException("No current challenge.");
        Assert.Null(before.OtpHmac);
        // The code the job sent before the release, when there was one, and otherwise any code.
        string code = ports.Sms.Received.Select(received => System.Text.RegularExpressions.Regex.Match(received.Message, "[0-9]{6}").Value).LastOrDefault() ?? "123456";

        List<string> actual = [];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            actual.Add(await (await browser.VerifyCodeAsync(code)).StatusAndBodyAsync());
        }

        Assert.Equal(expected, actual);
        Assert.Equal(3, (await factory.CurrentChallengeAsync(login))?.Attempts);
        Assert.DoesNotContain(await factory.LinksAsync(), link => link.LoginSub == login);
        List<AuditLogEntry> audits = await factory.VerifyAuditsAsync(login);
        Assert.Equal([AuditAction.LinkingOtpIncorrect, AuditAction.LinkingOtpIncorrect, AuditAction.LinkingLocked], audits.Select(entry => entry.Action));
        Assert.All(audits, entry => Assert.Equal((AuditResult.Rejected, before.CustomerId), (entry.Result, entry.CustomerId)));
    }

    [Fact]
    public async Task Verify_LoginWithNoChallenge_AnswersAFirstWrongCodeEveryTimeAndCountsNothing()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string firstWrongCode = (await WrongCodeAnswersAsync(factory, ports))[0];
        string login = LinkingApi.NewLogin("no-challenge");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        List<string> actual = [];
        for (int attempt = 0; attempt < 4; attempt++)
        {
            actual.Add(await (await browser.VerifyCodeAsync("123456")).StatusAndBodyAsync());
        }

        Assert.All(actual, answer => Assert.Equal(firstWrongCode, answer));
        Assert.Empty(await factory.ChallengesAsync(login));
        List<AuditLogEntry> audits = await factory.VerifyAuditsAsync(login);
        Assert.Equal(4, audits.Count);
        Assert.All(audits, entry => Assert.Equal((AuditAction.LinkingOtpIncorrect, AuditResult.Rejected, (string?)null), (entry.Action, entry.Result, entry.CustomerId)));
    }

    /// <summary>The three answers customer D's real challenge gives to wrong codes, from a login of its own.</summary>
    private static async Task<List<string>> WrongCodeAnswersAsync(ApiFactory factory, LinkingFakes.Ports ports)
    {
        string login = LinkingApi.NewLogin("reference");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string wrong = VerifyApi.WrongCode(await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D));
        List<string> answers = [];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            answers.Add(await (await browser.VerifyCodeAsync(wrong)).StatusAndBodyAsync());
        }

        return answers;
    }

    private static async Task StartAndWaitAsync(
        ApiFactory factory, HttpClient browser, string login, LinkingFakes.Customer customer, AuditAction stop, AuditResult result)
    {
        await browser.StartAcceptedAsync(customer);
        await factory.WaitForAuditAsync(login, stop, result);
    }
}
