using System.Buffers.Text;
using System.Net;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Withdraw;

/// <summary>
/// AC4: withdraw is on the customer routes, so the cookie alone is a credential, the login must be linked and an unsafe
/// request needs the antiforgery header, all before any lookup (AD-8, AD-10). Another customer's dispute is not found
/// and stays exactly as it was. The identical 404 bodies are in <c>Ownership/NotFoundResponsesTests</c>.
/// </summary>
public class WithdrawDisputeAuthorizationTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Withdraw_BearerTokenOnly_Returns401Unauthorized()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await OpenDisputeOfBAsync(factory);
        using HttpClient client = factory.CreateBrowser(new CookieContainer());
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.WithdrawPath(disputeId.ToString()), UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        // A JWT-shaped token built from its three parts at run time, so the source holds no token for gitleaks to flag.
        string token = string.Join('.', Base64Url.EncodeToString("""{"alg":"RS256"}"""u8), Base64Url.EncodeToString("""{"sub":"login-b"}"""u8), "c2lnbmF0dXJl");
        request.Headers.Add("Authorization", $"Bearer {token}");

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
        await AssertUntouchedAsync(factory, disputeId);
    }

    [Fact]
    public async Task Withdraw_UnlinkedLogin_Returns403LinkingRequired()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await OpenDisputeOfBAsync(factory);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemType.For(LinkingProblems.LinkingRequired), await response.ProblemTypeAsync());
        await AssertUntouchedAsync(factory, disputeId);
    }

    [Fact]
    public async Task Withdraw_OwnersCookieWithoutTheAntiforgeryHeader_Returns400ValidationAndWithdrawsNothing()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient owner = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        Guid disputeId = (await owner.RaiseCreatedAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"))).GetProperty("disputeId").GetGuid();

        HttpResponseMessage response = await owner.WithdrawAsync(disputeId.ToString(), withAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        await AssertUntouchedAsync(factory, disputeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Withdraw_AnotherCustomersOpenDispute_Returns404AndTheirDisputeStaysAsItWas(bool withNoteTooLong)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid theirs = await OpenDisputeOfBAsync(factory);
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await customerA.WithdrawAsync(
            theirs.ToString(), DisputesApi.WithdrawBody(withNoteTooLong ? new string('a', 501) : "Mine now"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await response.ProblemTypeAsync());
        await AssertUntouchedAsync(factory, theirs);
    }

    /// <summary>Customer B's Submitted dispute, raised by B.</summary>
    private static async Task<Guid> OpenDisputeOfBAsync(ApiFactory factory)
    {
        using HttpClient customerB = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        return (await customerB.RaiseCreatedAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"))).GetProperty("disputeId").GetGuid();
    }

    /// <summary>The dispute as its raise left it: version 1, Submitted, one stored event and one outbox row.</summary>
    private static async Task AssertUntouchedAsync(ApiFactory factory, Guid disputeId)
    {
        Assert.Equal([nameof(DisputeRaised)], (await factory.StreamAsync(disputeId)).Select(row => row.EventType));
        Assert.Single(await factory.OutboxRowsAsync(disputeId));
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Submitted, row.Status);
        Assert.Equal(1, row.Version);
    }
}
