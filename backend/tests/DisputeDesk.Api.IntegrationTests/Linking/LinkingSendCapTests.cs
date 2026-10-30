using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Linking;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.5 AC1 and AC2: at most 3 claimed sends per customer per hour, delivered or not, across logins and client IPs, held
/// under parallel starts by a transaction-level advisory lock on the customer that commits before the send.
/// </summary>
public class LinkingSendCapTests(PostgresFixture postgres)
{
    private static readonly IPAddress FirstIp = IPAddress.Parse("203.0.113.7");
    private static readonly IPAddress SecondIp = IPAddress.Parse("198.51.100.23");

    [Fact]
    public async Task Job_FourthStartWithinTheHourAfterTwoSentAndOneFailed_CapsWithoutSimSwapAndKeepsThe202()
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        var client = new ClientAddressStartupFilter(FirstIp);
        await using ApiFactory factory = await LinkingApi.StartApiAsync(
            postgres, ports, clock, services => services.AddTransient<IStartupFilter>(_ => client));
        using var events = new LinkingEvents(factory);
        await SendToDAsync(factory, "first");
        clock.Advance(TimeSpan.FromMinutes(10));
        client.Address = SecondIp;
        await SendToDAsync(factory, "second");
        clock.Advance(TimeSpan.FromMinutes(10));
        client.Address = FirstIp;
        ports.Sms.Failure = new IOException("The SMTP server went away.");
        string failedLogin = LinkingApi.NewLogin("failed");
        using (HttpClient failed = await factory.SignInUnlinkedAsync(failedLogin))
        {
            await failed.StartAcceptedAsync(LinkingFakes.D);
        }

        await factory.WaitForAuditAsync(failedLogin, AuditAction.LinkingOtpSent, AuditResult.Failed);
        DateTimeOffset failedClaim = clock.NowAsStored;
        ports.Sms.Failure = null;
        clock.Advance(TimeSpan.FromMinutes(10));
        client.Address = SecondIp;
        string login = LinkingApi.NewLogin("fourth");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        HttpResponseMessage response = await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingSendCapped, AuditResult.Rejected);
        Assert.Equal(
            [(AuditAction.LinkingStart, AuditResult.Succeeded, null, SecondIp), (AuditAction.LinkingSendCapped, AuditResult.Rejected, LinkingFakes.D.CustomerId, SecondIp)],
            entries.Select(entry => (entry.Action, entry.Result, entry.CustomerId, entry.ClientIp)));
        Assert.Equal(3, ports.Sms.Received.Count);
        Assert.Equal(3, ports.SimSwap.Cellphones.Count);
        LinkingChallenge capped = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Equal((null, null, null), (capped.CustomerId, capped.OtpHmac, capped.OtpSentAtUtc));
        Assert.Equal(1, events.Count("capped"));
        // The release cleared the OTP and its lifetime but kept the claim's instant, which holds its cap slot.
        LinkingChallenge released = Assert.Single(await factory.ChallengesAsync(failedLogin));
        Assert.Equal((null, null, failedClaim), (released.OtpHmac, released.ExpiresAtUtc, released.OtpSentAtUtc));
    }

    [Theory]
    [InlineData(3600, AuditAction.LinkingOtpSent)]
    [InlineData(3599, AuditAction.LinkingSendCapped)]
    public async Task Job_ThreeSendsThisLongAgo_CountsOnlySendsLessThanAnHourOld(int secondsLater, AuditAction expected)
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        // Microsecond-aligned, so a send stored now is exactly the instant an hour before the later start.
        clock.Advance(clock.NowAsStored - clock.GetUtcNow());
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        for (int send = 0; send < 3; send++)
        {
            await SendToDAsync(factory, $"earlier-{send}");
        }

        clock.Advance(TimeSpan.FromSeconds(secondsLater));
        string login = LinkingApi.NewLogin("later");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        await browser.StartAcceptedAsync(LinkingFakes.D);

        AuditResult result = expected == AuditAction.LinkingOtpSent ? AuditResult.Succeeded : AuditResult.Rejected;
        await factory.WaitForAuditAsync(login, expected, result);
        Assert.Equal(expected == AuditAction.LinkingOtpSent ? 4 : 3, ports.Sms.Received.Count);
    }

    [Fact]
    public async Task Job_SixParallelStartsWithTwoSendsAndTheFirstHeldBetweenCountAndClaim_ClaimsExactlyOneMore()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        await SendToDAsync(factory, "earlier-1");
        await SendToDAsync(factory, "earlier-2");
        LinkingFakes.Hold firstCheck = ports.SimSwap.HoldNext();
        string[] logins = [.. Enumerable.Range(0, 6).Select(start => LinkingApi.NewLogin($"parallel-{start}"))];
        HttpClient[] browsers = await Task.WhenAll(logins.Select(factory.SignInUnlinkedAsync));

        HttpResponseMessage[] responses = await Task.WhenAll(browsers.Select(browser => browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        // The held job sits between its count and its claim, holding the customer's lock; the other three running jobs
        // wait for the lock. Without it they would count 2 sends, pass and claim.
        await firstCheck.Reached;
        await factory.WaitForLockWaitersAsync(3);
        firstCheck.Release();
        List<AuditLogEntry> stops = await factory.WaitForStopsAsync(logins, [AuditAction.LinkingOtpSent, AuditAction.LinkingSendCapped], count: 6);
        Assert.Equal(1, stops.Count(entry => entry is { Action: AuditAction.LinkingOtpSent, Result: AuditResult.Succeeded }));
        Assert.Equal(5, stops.Count(entry => entry is { Action: AuditAction.LinkingSendCapped, Result: AuditResult.Rejected }));
        Assert.Equal(6, stops.Count);
        Assert.Equal(3, ports.Sms.Received.Count);
        Array.ForEach(browsers, browser => browser.Dispose());
    }

    [Fact]
    public async Task Job_CustomerLockHeldPastTheBudget_AuditsLinkingStartFailedAndCallsNoSimSwap()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(
            postgres, ports, new TestClock(), settings: new Dictionary<string, string?> { ["Linking:Budget"] = "00:00:01" });
        string login = LinkingApi.NewLogin("lock-wait");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            // Another holder of customer D's lock, taken the way the job takes it.
            AppDbContext holder = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using IDbContextTransaction transaction = await holder.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await holder.Database.LockCustomerAsync(LinkingFakes.D.CustomerId, TestContext.Current.CancellationToken);

            await browser.StartAcceptedAsync(LinkingFakes.D);

            List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingStart, AuditResult.Failed);
            Assert.Equal(LinkingFakes.D.CustomerId, entries[^1].CustomerId);
            Assert.Empty(ports.SimSwap.Cellphones);
            Assert.Null(Assert.Single(await factory.ChallengesAsync(login)).OtpSentAtUtc);
        }

        await browser.StartAcceptedAsync(LinkingFakes.D);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
    }

    [Fact]
    public async Task Job_HoldsTheTwoKeyCustomerLockAtTheSimSwapCheckAndNoLockAndNoOpenTransactionWhileSending()
    {
        var ports = new LinkingFakes.Ports();
        LinkingFakes.Hold check = ports.SimSwap.HoldNext();
        LinkingFakes.Hold send = ports.Sms.HoldNext();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("sending");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(LinkingFakes.D);
        await check.Reached;
        Assert.Equal(1, await factory.AdvisoryLocksAsync(granted: true));
        check.Release();
        await send.Reached;

        Assert.Equal(0, await factory.AdvisoryLocksAsync(granted: true));
        Assert.Equal(0, await factory.IdleInTransactionAsync());
        Assert.NotNull(Assert.Single(await factory.ChallengesAsync(login)).OtpSentAtUtc);
        send.Release();
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
    }

    /// <summary>A new login starts linking to customer D and its OTP is sent.</summary>
    private static async Task SendToDAsync(ApiFactory factory, string prefix)
    {
        string login = LinkingApi.NewLogin(prefix);
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        await browser.StartAcceptedAsync(LinkingFakes.D);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
    }
}
