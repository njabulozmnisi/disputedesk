using System.Net;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.2 AC5: the verify route in every authorisation state, checked 401, 403, then 400 (AD-8, AD-10), and no refused call
/// counts an attempt or writes an audit entry.
/// </summary>
public class VerifyOtpAuthorizationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Verify_Anonymous_Returns401UnauthorizedWithoutRedirect()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await anonymous.VerifyAsync(VerifyApi.Body("123456"), withAntiforgeryToken: false);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, CommonProblems.Unauthorized);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Verify_BearerTokenOnlyNoCookie_Returns401UnauthorizedWithoutRedirect()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await anonymous.VerifyAsync(
            VerifyApi.Body("123456"), withAntiforgeryToken: false, request => request.Headers.Add("Authorization", "Bearer some-access-token"));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, CommonProblems.Unauthorized);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_LinkedCustomer_Returns403AlreadyLinkedBeforeValidationAndWritesNothing(bool wellFormedBody)
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        string login = LinkingApi.NewLogin("linked");
        using HttpClient browser = await factory.SignInLinkedAsync(login, TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.VerifyAsync(wellFormedBody ? VerifyApi.Body("123456") : """{"code":"x"}""");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, LinkingProblems.AlreadyLinked);
        Assert.Empty(await factory.LinkingAuditsAsync(login));
    }

    [Fact]
    public async Task Verify_UnlinkedWithoutAntiforgeryToken_Returns400AndCountsNothing()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        string login = LinkingApi.NewLogin("no-token");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);

        HttpResponseMessage response = await browser.VerifyAsync(VerifyApi.Body(otp), withAntiforgeryToken: false);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, CommonProblems.Validation);
        Assert.Equal(0, (await factory.CurrentChallengeAsync(login))?.Attempts);
        Assert.Empty(await factory.VerifyAuditsAsync(login));
        Assert.Empty(await factory.LinksAsync());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string slug)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemType.For(slug), await response.ProblemTypeAsync());
    }
}
