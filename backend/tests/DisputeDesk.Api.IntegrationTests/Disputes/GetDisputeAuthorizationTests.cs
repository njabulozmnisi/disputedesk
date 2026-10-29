using System.Buffers.Text;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC4: the detail is on the customer routes (<c>Linked</c>, cookie only), so the checks run 401, 403, then 400 for a
/// malformed ID (AD-8, AD-10), and no call writes an audit row, refused ones included (AD-14a).
/// </summary>
public class GetDisputeAuthorizationTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private static readonly string AnyDisputeId = Guid.CreateVersion7().ToString();

    [Fact]
    public async Task GetDispute_Anonymous_Returns401UnauthorizedWithoutRedirectOrChallengeAndNoAuditRow()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        int auditRows = await AuditRowCountAsync(factory);

        HttpResponseMessage response = await browser.GetDisputeAsync(AnyDisputeId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
        Assert.Equal(auditRows, await AuditRowCountAsync(factory));
    }

    [Fact]
    public async Task GetDispute_UnlinkedLogin_Returns403LinkingRequiredAndNoAuditRow()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");
        int auditRows = await AuditRowCountAsync(factory);

        HttpResponseMessage response = await browser.GetDisputeAsync(AnyDisputeId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(ProblemType.For(LinkingProblems.LinkingRequired), await response.ProblemTypeAsync());
        Assert.Equal(auditRows, await AuditRowCountAsync(factory));
    }

    [Fact]
    public async Task GetDispute_UnlinkedLoginWithDisputeIdNotAGuid_Returns403LinkingRequiredBefore400()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "unlinked-login");
        int auditRows = await AuditRowCountAsync(factory);

        HttpResponseMessage response = await browser.GetDisputeAsync("not-a-guid");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemType.For(LinkingProblems.LinkingRequired), await response.ProblemTypeAsync());
        Assert.Equal(auditRows, await AuditRowCountAsync(factory));
    }

    [Fact]
    public async Task GetDispute_LinkedCustomer_Returns200AndNoAuditRow()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetString() ?? "";
        int auditRows = await AuditRowCountAsync(factory);

        HttpResponseMessage response = await browser.GetDisputeAsync(disputeId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(auditRows, await AuditRowCountAsync(factory));
    }

    [Fact]
    public async Task GetDispute_LinkedCustomerOnAnUnknownDispute_Returns404AndNoAuditRow()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        int auditRows = await AuditRowCountAsync(factory);

        HttpResponseMessage response = await browser.GetDisputeAsync(AnyDisputeId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(auditRows, await AuditRowCountAsync(factory));
    }

    [Fact]
    public async Task GetDispute_BearerTokenOnly_Returns401Unauthorized()
    {
        // The customer routes take only the session cookie (AD-8); a bearer token is not a credential here.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient client = factory.CreateBrowser(new CookieContainer());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{DisputesApi.RaisePath}/{AnyDisputeId}", UriKind.Relative));
        // A JWT-shaped token built from its three parts at run time, so the source holds no token for gitleaks to flag.
        string token = string.Join('.', Base64Url.EncodeToString("""{"alg":"RS256"}"""u8), Base64Url.EncodeToString("""{"sub":"login-a"}"""u8), "c2lnbmF0dXJl");
        request.Headers.Add("Authorization", $"Bearer {token}");
        int auditRows = await AuditRowCountAsync(factory);

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
        Assert.Equal(auditRows, await AuditRowCountAsync(factory));
    }

    /// <summary>Every <c>AuditLogEntries</c> row, of any action, so a read that audited anything would change it.</summary>
    private static async Task<int> AuditRowCountAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>()
            .CountAsync(TestContext.Current.CancellationToken);
    }
}
