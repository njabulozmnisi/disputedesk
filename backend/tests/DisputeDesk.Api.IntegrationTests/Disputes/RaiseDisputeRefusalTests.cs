using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC2 and AC4: the refusals and their order, 404, 409, 422, then 400 for an expected amount not below the transaction
/// amount (AD-8). Body-shape refusals come before the lookup and are in <see cref="RaiseDisputeInputTests"/>.
/// </summary>
public class RaiseDisputeRefusalTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Theory]
    [InlineData("ACC-A-1", "TXN-A-9999")]  // no such transaction
    [InlineData("ACC-B-1", "TXN-B-0027")]  // customer B's disputable transaction
    [InlineData("ACC-A-2", "TXN-A-0029")]  // A's transaction under A's other account
    [InlineData("ACC-A-1", "txn-a-0029")]  // not shaped like an ID
    [InlineData("ACC-A-1", "TXN-A-0029 ")] // not shaped like an ID
    [InlineData("ACC-A-1", "TXN-Ä-0029")]  // not shaped like an ID, and not ASCII
    [InlineData("ACC-A-1", "TXN-A-0029-TXN-A-0029-TXN-A-0029")] // 32 characters, the longest allowed
    public async Task Raise_MissingOrAnotherCustomersTransaction_Returns404NotFoundAndWritesNothing(string accountRef, string transactionId)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(transactionId, accountRef));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await response.ProblemTypeAsync());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_AnotherCustomersDisputedTransaction_Returns404NotAlreadyDisputed()
    {
        // 409 comes only after the ownership check, so B's dispute is not revealed to A (2.7 security review).
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient customerB = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        Assert.Equal(HttpStatusCode.Created, (await customerB.RaiseAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"))).StatusCode);
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await customerA.RaiseAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await response.ProblemTypeAsync());
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_SameTransactionTwice_Returns409AlreadyDisputedWithTheDisputeAndWritesNothingMore()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string disputeId = await RaisedDisputeIdAsync(browser);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(reason: "duplicateCharge"));

        await response.AssertAlreadyDisputedAsync(disputeId, "submitted");
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Theory]
    [InlineData(DisputeStatus.UnderReview, "underReview")]
    [InlineData(DisputeStatus.Escalated, "underReviewPrioritised")]
    [InlineData(DisputeStatus.Resolved, "resolved")]
    [InlineData(DisputeStatus.Withdrawn, "withdrawn")]
    public async Task Raise_TransactionWhoseDisputeIsInAnyStatus_Returns409AlreadyDisputedWithItsCustomerStatus(DisputeStatus status, string customerStatus)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string disputeId = await RaisedDisputeIdAsync(browser);
        await factory.SetStatusAsync(status);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        await response.AssertAlreadyDisputedAsync(disputeId, customerStatus);
    }

    [Theory]
    [InlineData("ACC-A-1", DisputesApi.Pending, "notEligible")]
    [InlineData("ACC-A-1", "TXN-A-0002", "notEligible")]  // a credit
    [InlineData("ACC-A-1", "TXN-A-0024", "notEligible")]  // an own-account transfer
    [InlineData("ACC-A-1", DisputesApi.Old, "outsideWindow")]
    public async Task Raise_NotDisputable_Returns422NotDisputableWithTheReason(string accountRef, string transactionId, string reason)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(transactionId, accountRef));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(DisputeProblems.NotDisputable), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(reason, problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Theory]
    [InlineData(DisputesApi.DisputableAmount)]
    [InlineData("87.24")]
    [InlineData("99999999999999999.99")] // the largest the pattern allows
    public async Task Raise_ExpectedAmountNotBelowTheTransactionAmount_Returns400NamingTheExpectedAmount(string expected)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: expected));

        await response.AssertValidationProblemAsync("expectedAmount");
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Theory]
    [InlineData("ACC-B-1", "TXN-B-0027", HttpStatusCode.NotFound)]
    [InlineData("ACC-A-1", DisputesApi.Pending, HttpStatusCode.UnprocessableEntity)]
    [InlineData("ACC-A-1", DisputesApi.Old, HttpStatusCode.UnprocessableEntity)]
    public async Task Raise_ExpectedAmountNotBelowOnAnotherRefusal_ReturnsTheEarlierRefusal(string accountRef, string transactionId, HttpStatusCode expected)
    {
        // AD-8: the amount rule needs the transaction, so it runs last: 404 and 422 win over it.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(transactionId, accountRef, "wrongAmount", expectedAmount: "100000.00"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Raise_ExpectedAmountNotBelowOnAnEverDisputedTransaction_Returns409()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Assert.Equal(HttpStatusCode.Created, (await browser.RaiseAsync(DisputesApi.Body())).StatusCode);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: "100000.00"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<string> RaisedDisputeIdAsync(HttpClient browser)
    {
        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        return body.RootElement.GetProperty("disputeId").GetString() ?? "";
    }
}

/// <summary>Moves the one dispute to another status, as later stories' actions would, to show a rule holds in every status.</summary>
public static class DisputeStatusSetter
{
    public static async Task SetStatusAsync(this ApiFactory factory, DisputeStatus status)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<Dispute>().IgnoreQueryFilters()
            .ExecuteUpdateAsync(setters => setters.SetProperty(dispute => dispute.Status, status), TestContext.Current.CancellationToken);
    }
}
