using System.Net;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC3: the start in every authorisation state, checked 401, 403, then 400 (AD-8, AD-10), and no refused call
/// writes a challenge.
/// </summary>
public class StartLinkingAuthorizationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Start_Anonymous_Returns401UnauthorizedWithoutRedirect()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await anonymous.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D), withAntiforgeryToken: false);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, CommonProblems.Unauthorized);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Start_BearerTokenOnlyNoCookie_Returns401UnauthorizedWithoutRedirect()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await anonymous.StartLinkingAsync(
            LinkingApi.Body(LinkingFakes.D), withAntiforgeryToken: false, request => request.Headers.Add("Authorization", "Bearer some-access-token"));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, CommonProblems.Unauthorized);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_LinkedCustomer_Returns403AlreadyLinkedBeforeValidationAndWritesNothing(bool wellFormedBody)
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        string login = LinkingApi.NewLogin("linked");
        using HttpClient browser = await factory.SignInLinkedAsync(login, TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.StartLinkingAsync(wellFormedBody ? LinkingApi.Body(LinkingFakes.D) : """{"idNumber":"x"}""");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, LinkingProblems.AlreadyLinked);
        Assert.Empty(await factory.ChallengesAsync(login));
        Assert.Empty(await factory.LinkingAuditsAsync(login));
    }

    [Fact]
    public async Task Start_UnlinkedWithoutAntiforgeryToken_Returns400AndWritesNothing()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        string login = LinkingApi.NewLogin("no-token");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        HttpResponseMessage response = await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D), withAntiforgeryToken: false);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, CommonProblems.Validation);
        Assert.Empty(await factory.ChallengesAsync(login));
    }

    [Fact]
    public async Task Start_UnlinkedWithValidBody_Returns202()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
        string login = LinkingApi.NewLogin("unlinked");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        HttpResponseMessage response = await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.D));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await factory.ChallengesAsync(login));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string slug)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemType.For(slug), await response.ProblemTypeAsync());
    }
}
