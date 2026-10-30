using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC4: the job runs the core-banking match, the link check, then the SIM-swap check on the cellphone on the bank's
/// record, and only then claims the send; a failed SIM-swap check, whatever its cause, sends nothing (NIST SP 800-63B
/// §3.1.3.3).
/// </summary>
public class LinkingCheckOrderTests(PostgresFixture postgres)
{
    public static TheoryData<string> FailedSimSwapChecks => ["recent swap", "error", "timeout"];

    [Theory]
    [MemberData(nameof(FailedSimSwapChecks))]
    public async Task Job_SimSwapCheckFails_SendsNothingLeavesTheChallengeBlankAndAuditsSimSwapFailed(string failure)
    {
        var ports = new LinkingFakes.Ports();
        LinkingFakes.Customer customer = failure == "recent swap" ? LinkingFakes.C : LinkingFakes.D;
        ports.SimSwap.Answer = failure switch
        {
            "error" => LinkingFakes.SimSwapAnswer.Error,
            "timeout" => LinkingFakes.SimSwapAnswer.Timeout,
            _ => LinkingFakes.SimSwapAnswer.FromFixtures,
        };
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("sim-swap");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(customer);

        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingSimSwapFailed, AuditResult.Rejected);
        Assert.Equal(
            [(AuditAction.LinkingStart, AuditResult.Succeeded, null), (AuditAction.LinkingSimSwapFailed, AuditResult.Rejected, customer.CustomerId)],
            entries.Select(entry => (entry.Action, entry.Result, entry.CustomerId)));
        Assert.Equal([customer.Cellphone], ports.SimSwap.Cellphones);
        Assert.Empty(ports.Sms.Received);
        LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Equal((null, null, null, null), (challenge.CustomerId, challenge.OtpHmac, challenge.OtpSentAtUtc, challenge.ExpiresAtUtc));
    }

    [Fact]
    public async Task Job_WhileTheSimSwapCheckRuns_HasNotClaimedTheSend()
    {
        // The claim comes after the check: while the check is held the challenge is still blank.
        var ports = new LinkingFakes.Ports();
        LinkingFakes.Hold check = ports.SimSwap.HoldNext();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("order");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(LinkingFakes.D);
        await check.Reached;

        Assert.Equal(1, ports.CoreBanking.Calls);
        LinkingChallenge held = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Null(held.OtpSentAtUtc);
        Assert.Empty(ports.Sms.Received);
        check.Release();
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        Assert.NotNull(Assert.Single(await factory.ChallengesAsync(login)).OtpSentAtUtc);
    }

    [Fact]
    public async Task Job_NoMatch_StopsBeforeTheSimSwapCheck()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("no-match");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(LinkingFakes.NoMatch);

        await factory.WaitForAuditAsync(login, AuditAction.LinkingNoMatch, AuditResult.Rejected);
        Assert.Empty(ports.SimSwap.Cellphones);
        Assert.Empty(ports.Sms.Received);
    }

    [Fact]
    public async Task Job_CustomerAlreadyLinked_StopsBeforeTheSimSwapCheck()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        await factory.LinkAsync(LinkingApi.NewLogin("customer-a"), LinkingFakes.A.CustomerId);
        string login = LinkingApi.NewLogin("already-linked");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(LinkingFakes.A);

        await factory.WaitForAuditAsync(login, AuditAction.LinkingAlreadyLinked, AuditResult.Rejected);
        Assert.Empty(ports.SimSwap.Cellphones);
        Assert.Empty(ports.Sms.Received);
    }

    [Fact]
    public async Task Job_RequestCarriesAnotherCellphone_ChecksAndSendsOnlyTheBanksNumber()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("extra-cellphone");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        HttpResponseMessage response = await browser.StartLinkingAsync(
            $$"""{"idNumber":"{{LinkingFakes.D.IdNumber}}","accountNumber":"{{LinkingFakes.D.AccountNumber}}","cellphone":"+27829999999"}""");

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        Assert.Equal([LinkingFakes.D.Cellphone], ports.SimSwap.Cellphones);
        Assert.Equal(LinkingFakes.D.Cellphone, Assert.Single(ports.Sms.Received).Cellphone);
    }
}
