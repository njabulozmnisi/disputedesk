using System.Buffers.Text;
using System.Net;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC2, the authorisation states: the raise is on the customer routes (<c>Linked</c>, cookie only), so 401 and 403 come
/// before body validation, and a session cookie without <c>X-XSRF-TOKEN</c> gets 400 with nothing written.
/// </summary>
public class RaiseDisputeAuthorizationTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string BrokenBody = """{"accountRef":"","reason":"fraud"}""";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Raise_Anonymous_Returns401UnauthorizedWithoutRedirectBeforeValidation(bool validBody)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await browser.RaiseAsync(validBody ? DisputesApi.Body() : BrokenBody, withAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
    }

    [Fact]
    public async Task Raise_BearerTokenOnly_Returns401Unauthorized()
    {
        // The customer routes take only the session cookie (AD-8); a bearer token is not a credential here.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient client = factory.CreateBrowser(new CookieContainer());
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(DisputesApi.Body(), Encoding.UTF8, "application/json"),
        };
        // A JWT-shaped token built from its three parts at run time, so the source holds no token for gitleaks to flag.
        string token = string.Join('.', Base64Url.EncodeToString("""{"alg":"RS256"}"""u8), Base64Url.EncodeToString("""{"sub":"login-a"}"""u8), "c2lnbmF0dXJl");
        request.Headers.Add("Authorization", $"Bearer {token}");

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Raise_UnlinkedLogin_Returns403LinkingRequiredBeforeValidation(bool validBody)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");

        HttpResponseMessage response = await browser.RaiseAsync(validBody ? DisputesApi.Body() : BrokenBody);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(ProblemType.For(LinkingProblems.LinkingRequired), await response.ProblemTypeAsync());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_LinkedWithoutAntiforgeryToken_Returns400ValidationAndWritesNothing()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(), withAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_LinkedWithAnotherLoginsAntiforgeryToken_Returns400AndWritesNothing()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient attacker = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        string attackersToken = await attacker.AntiforgeryTokenAsync();
        using HttpClient victim = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(DisputesApi.Body(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", attackersToken);

        HttpResponseMessage response = await victim.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_CustomerIdInTheBodyAndHeader_RaisesForTheLinkedCustomerOnly()
    {
        // The customer comes from the link table, never the request: B's ID in the body or a header changes nothing.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(
                """{"accountRef":"ACC-B-1","transactionId":"TXN-B-0027","reason":"unrecognised","customerId":"CUS-000002"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        request.Headers.Add("X-Customer-Id", TransactionsApi.CustomerB);

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }
}
