using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC3: two raises of one transaction at once. Both are held after their "already disputed?" read until both have
/// read nothing, so only <c>UQ_Disputes_AccountRef_TransactionId</c> can stop the second, and it must become 409.
/// </summary>
public class RaiseDisputeConcurrencyTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Raise_TwoAtOncePastTheCheck_OneReturns201AndTheOther409AlreadyDisputedFromTheUniqueIndex()
    {
        // The handler's read of an earlier dispute is the first query on Disputes a raise sends; the loser's re-read
        // of the winner comes after the rendezvous and passes.
        var rendezvous = new RendezvousInterceptor("FROM \"Disputes\"", parties: 2);
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(rendezvous)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(
            browser.RaiseAsync(DisputesApi.Body(), token),
            browser.RaiseAsync(DisputesApi.Body(reason: "duplicateCharge"), token));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        HttpResponseMessage winner = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
        using JsonDocument created = await winner.ReadJsonAsync();
        HttpResponseMessage loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        // The loser re-reads the winning dispute (the customer's own) and names it, as the check before the save does.
        await loser.AssertAlreadyDisputedAsync(created.RootElement.GetProperty("disputeId").GetString() ?? "", "submitted");
        // After losing the race the refusal is audited on the cleared change tracker, naming the winner (AD-14a).
        Assert.Equal(RowCounts.One with { AuditLogEntries = 1 }, await factory.CountRowsAsync());
        Assert.Single(await factory.DisputeAuditRowsAsync())
            .AssertRejectedOfA(AuditAction.Raise, stub.NowAsStored, created.RootElement.GetProperty("disputeId").GetGuid());
    }

    [Fact]
    public async Task Raise_FiveInParallel_ExactlyOneCommitsWithOneRowInEachTable()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => browser.RaiseAsync(DisputesApi.Body(), token)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(response => response.StatusCode != HttpStatusCode.Created),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        // Each of the four 409s is audited once (AD-14a).
        Assert.Equal(RowCounts.One with { AuditLogEntries = 4 }, await factory.CountRowsAsync());
    }
}
