using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>AC6: core banking still failing after the resilience policies gives 503 <c>core-banking-unavailable</c>.</summary>
public class TransactionsOutageTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ListTransactions_CoreBankingFailing_Returns503AfterTheRetries()
    {
        await using FakeCoreBankingServer failingStub = await FakeCoreBankingServer.FailingAsync();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, failingStub.BaseUrl, TimeProvider.System.GetUtcNow());
        await factory.LinkAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-a");

        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(TransactionProblems.CoreBankingUnavailable), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(3, failingStub.RequestCount); // the first attempt and two retries
    }

    [Fact]
    public async Task ListTransactions_CoreBankingUnreachable_Returns503WithinFiveSeconds()
    {
        // Nothing listens on port 1, as when the core-banking container is stopped.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, new Uri("http://127.0.0.1:1"), TimeProvider.System.GetUtcNow());
        await factory.LinkAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-a");

        var elapsed = Stopwatch.StartNew();
        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/transactions");
        elapsed.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(TransactionProblems.CoreBankingUnavailable), problem.RootElement.GetProperty("type").GetString());
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5.5), $"Took {elapsed.Elapsed}.");
    }
}
