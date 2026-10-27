using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>AC1 and AC2: the three authorisation states, and the customer taken only from the link table.</summary>
public class TransactionsAuthorizationTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string Path = "/api/v1/transactions?limit=50";

    [Fact]
    public async Task ListTransactions_Anonymous_Returns401UnauthorizedWithoutRedirect()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await browser.GetPathAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        await AssertProblemTypeAsync(response, CommonProblems.Unauthorized);
    }

    [Fact]
    public async Task ListTransactions_UnlinkedLogin_Returns403LinkingRequiredWithoutRedirect()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");

        HttpResponseMessage response = await browser.GetPathAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        await AssertProblemTypeAsync(response, LinkingProblems.LinkingRequired);
    }

    [Fact]
    public async Task ListTransactions_UnlinkedLoginWithLimit51_Returns403LinkingRequired()
    {
        // The authorisation state is checked before the query (FR-24's order: 401, 403, then 400).
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");

        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/transactions?limit=51");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemTypeAsync(response, LinkingProblems.LinkingRequired);
    }

    [Fact]
    public async Task ListTransactions_LinkedCustomer_Returns200WithTheirOwnTransactionsOnly()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        await factory.LinkAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-a");

        HttpResponseMessage response = await browser.GetPathAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["ACC-A-1", "ACC-A-2"], await AccountRefsAsync(response));
    }

    [Fact]
    public async Task ListTransactions_CustomerIdInQueryAndHeader_StillReturnsTheLinkedCustomer()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        await factory.LinkAsync("login-b", TransactionsApi.CustomerB);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-b");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{Path}&customerId={TransactionsApi.CustomerA}", UriKind.Relative));
        request.Headers.Add("X-Customer-Id", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["ACC-B-1"], await AccountRefsAsync(response));
    }

    [Fact]
    public async Task ListTransactions_TokenSubjectEqualsAnotherCustomersId_ReturnsTheCustomerLinkedToThatLogin()
    {
        // The login's sub happens to read like customer A's ID, but the link table says this login is customer B.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        await factory.LinkAsync(TransactionsApi.CustomerA, TransactionsApi.CustomerB);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.GetPathAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["ACC-B-1"], await AccountRefsAsync(response));
    }

    [Fact]
    public async Task ListTransactions_UnlinkedTokenSubjectEqualsACustomerId_Returns403LinkingRequired()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.GetPathAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemTypeAsync(response, LinkingProblems.LinkingRequired);
    }

    private static async Task AssertProblemTypeAsync(HttpResponseMessage response, string slug)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(slug), problem.RootElement.GetProperty("type").GetString());
    }

    private static async Task<string[]> AccountRefsAsync(HttpResponseMessage response)
    {
        using JsonDocument page = await response.ReadJsonAsync();
        return [.. page.RootElement.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("accountRef").GetString() ?? "")
            .Distinct()
            .Order(StringComparer.Ordinal)];
    }
}
