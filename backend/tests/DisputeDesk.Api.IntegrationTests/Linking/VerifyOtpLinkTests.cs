using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.2 AC1 and AC2: a correct code consumes the login's current challenge and links it to that challenge's customer in
/// one transaction, and the same session goes on as the linked customer.
/// </summary>
public class VerifyOtpLinkTests(PostgresFixture postgres)
{
    private static readonly IPAddress StoredClientIp = IPAddress.Parse("203.0.113.7");

    [Fact]
    public async Task Verify_CorrectCode_Returns204AndLinksTheLoginToTheChallengesCustomer()
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        string login = LinkingApi.NewLogin("links");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);

        HttpResponseMessage response = await browser.VerifyCodeAsync(otp);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Equal(clock.NowAsStored, challenge.ConsumedAtUtc);
        Assert.Equal(1, challenge.Attempts);
        CustomerLink link = Assert.Single(await factory.LinksAsync());
        Assert.Equal((login, LinkingFakes.D.CustomerId, clock.NowAsStored), (link.LoginSub, link.CustomerId, link.LinkedAtUtc));
        AuditLogEntry linked = Assert.Single(await factory.VerifyAuditsAsync(login));
        Assert.Equal((AuditAction.Linked, AuditResult.Succeeded, LinkingFakes.D.CustomerId), (linked.Action, linked.Result, linked.CustomerId));
        Assert.Equal((ActorType.Customer, login, StoredClientIp), (linked.ActorType, linked.ActorId, linked.ClientIp));
    }

    [Fact]
    public async Task Verify_CorrectCode_KeepsTheSessionAndItsNextRequestIsTheLinkedCustomer()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("same-session");
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        await browser.SignInAsync(factory.Zitadel, login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        string cookieBefore = SessionCookie(cookies);
        Session sessionBefore = await SessionAsync(factory, login);

        HttpResponseMessage response = await browser.VerifyCodeAsync(otp);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(cookieBefore, SessionCookie(cookies));
        Session sessionAfter = await SessionAsync(factory, login);
        Assert.Equal(sessionBefore.KeyHash, sessionAfter.KeyHash);
        Assert.Equal(
            (sessionBefore.Id, sessionBefore.LoginSub, sessionBefore.AbsoluteExpiresAtUtc),
            (sessionAfter.Id, sessionAfter.LoginSub, sessionAfter.AbsoluteExpiresAtUtc));

        HttpResponseMessage transactions = await browser.GetPathAsync("/api/v1/transactions");
        Assert.Equal(HttpStatusCode.OK, transactions.StatusCode);
        Assert.Equal([LinkingFakes.D.CustomerId], ports.CoreBanking.TransactionReads);
        Assert.Equal("linked", await browser.SessionStateAsync());
        HttpResponseMessage start = await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D));
        Assert.Equal(ProblemType.For(LinkingProblems.AlreadyLinked), await start.ProblemTypeAsync());
        HttpResponseMessage verify = await browser.VerifyCodeAsync(otp);
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal(ProblemType.For(LinkingProblems.AlreadyLinked), await verify.ProblemTypeAsync());
    }

    [Fact]
    public async Task Logout_AfterLinking_StillSendsTheIdTokenHint()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("logout-after");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.VerifyCodeAsync(otp)).StatusCode);

        HttpResponseMessage logout = await browser.LogoutAsync(await browser.AntiforgeryTokenAsync());

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        // Zitadel's end-session check throws unless id_token_hint is the ID token it issued at sign-in.
        factory.Zitadel.EndSession(Assert.IsType<Uri>(logout.Headers.Location));
    }

    [Fact]
    public async Task Verify_CorrectCodeWithFieldsNamingAnotherCustomer_IgnoresThemAndLinksTheChallengesCustomer()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("extra-fields");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        var otherChallenge = Guid.NewGuid();

        HttpResponseMessage response = await browser.VerifyAsync(JsonSerializer.Serialize(
            new { code = otp, customerId = LinkingFakes.A.CustomerId, challengeId = otherChallenge, loginSub = "someone-else" }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        CustomerLink link = Assert.Single(await factory.LinksAsync());
        Assert.Equal((login, LinkingFakes.D.CustomerId), (link.LoginSub, link.CustomerId));
    }

    [Fact]
    public async Task Verify_CorrectCodeOfASupersededChallenge_AnswersAsAWrongCodeOnTheCurrentOneAndLinksNothing()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("superseded");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string firstOtp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        await browser.StartAcceptedAsync(LinkingFakes.NoMatch);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingNoMatch, AuditResult.Rejected);

        HttpResponseMessage response = await browser.VerifyCodeAsync(firstOtp);

        await response.AssertOtpIncorrectAsync(attemptsRemaining: 2);
        Assert.Empty(await factory.LinksAsync());
        // Both starts share the test clock's instant, so the two rows are told apart by state, not by order.
        List<LinkingChallenge> challenges = await factory.ChallengesAsync(login);
        Assert.Equal(0, Assert.Single(challenges, challenge => challenge.SupersededAtUtc is not null).Attempts);
        Assert.Equal(1, Assert.Single(challenges, challenge => challenge.SupersededAtUtc is null).Attempts);
        Assert.All(challenges, challenge => Assert.Null(challenge.ConsumedAtUtc));
    }

    [Theory]
    [InlineData(-TimeSpan.TicksPerMicrosecond, HttpStatusCode.NoContent)]
    [InlineData(0, HttpStatusCode.UnprocessableEntity)]
    public async Task Verify_CorrectCodeAroundTheStoredExpiry_LinksOneMicrosecondBeforeAndIsExpiredAtIt(long ticksFromExpiry, HttpStatusCode expected)
    {
        var ports = new LinkingFakes.Ports();
        var clock = new TestClock();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, clock);
        string login = LinkingApi.NewLogin("expiry");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        DateTimeOffset expiresAt = (await factory.CurrentChallengeAsync(login))?.ExpiresAtUtc ?? throw new InvalidOperationException("Not claimed.");
        clock.Advance(expiresAt.AddTicks(ticksFromExpiry) - clock.GetUtcNow());

        HttpResponseMessage response = await browser.VerifyCodeAsync(otp);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.UnprocessableEntity)
        {
            await response.AssertOtpIncorrectAsync(attemptsRemaining: 2);
            Assert.Empty(await factory.LinksAsync());
            AuditLogEntry expired = Assert.Single(await factory.VerifyAuditsAsync(login));
            Assert.Equal((AuditAction.LinkingOtpExpired, AuditResult.Rejected, LinkingFakes.D.CustomerId), (expired.Action, expired.Result, expired.CustomerId));
        }
    }

    [Fact]
    public async Task Verify_CorrectCodeWhenTheLinkedAuditEntryFails_CommitsNeitherTheLinkNorTheConsume()
    {
        var ports = new LinkingFakes.Ports();
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        await using ApiFactory factory = await LinkingApi.StartApiAsync(
            postgres, ports, new TestClock(), services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit)));
        string login = LinkingApi.NewLogin("link-audit-fails");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        failAudit.Armed = true;

        HttpResponseMessage response = await browser.VerifyCodeAsync(otp);
        failAudit.Armed = false;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, failAudit.Failed);
        Assert.Empty(await factory.LinksAsync());
        LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Null(challenge.ConsumedAtUtc);
    }

    private static string SessionCookie(CookieContainer cookies) =>
        cookies.GetAllCookies().Single(cookie => cookie.Name == ApiFactory.SessionCookieName).Value;

    private static async Task<Session> SessionAsync(ApiFactory factory, string login)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Session>().AsNoTracking()
            .SingleAsync(session => session.LoginSub == login, TestContext.Current.CancellationToken);
    }
}
