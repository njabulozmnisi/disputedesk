using System.Net;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>AC5: with core banking unreachable or failing, a raise gets 503 <c>core-banking-unavailable</c> and writes nothing.</summary>
public class RaiseDisputeOutageTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Raise_CoreBankingUnreachable_Returns503AndWritesNothing()
    {
        // Nothing listens on port 1, as when the core-banking container is stopped.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, new Uri("http://127.0.0.1:1"), TimeProvider.System.GetUtcNow());
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ProblemType.For(TransactionProblems.CoreBankingUnavailable), await response.ProblemTypeAsync());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_CoreBankingAnsweringBrokenJson_Returns503AndWritesNothing()
    {
        await using FakeCoreBankingServer brokenStub = await FakeCoreBankingServer.MalformedAsync();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, brokenStub.BaseUrl, TimeProvider.System.GetUtcNow());
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }
}
