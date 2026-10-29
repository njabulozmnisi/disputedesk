using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;

/// <summary>
/// AC5: keys are scoped per customer and kept 24 hours, after which the row is replaced; the completion is conditional
/// on the lease, so a lost lease rolls the dispute back, and a key whose dispute committed is never released.
/// </summary>
public class IdempotencyScopeAndLeaseTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Raise_TwoCustomersWithTheSameKey_EachRaisesTheirOwnDispute()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient customerB = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        string key = DisputesApi.NewKey();

        HttpResponseMessage a = await customerA.RaiseWithKeyAsync(DisputesApi.Body(), key);
        HttpResponseMessage b = await customerB.RaiseWithKeyAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"), key);

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        Assert.Equal(
            [TransactionsApi.CustomerA, TransactionsApi.CustomerB],
            (await factory.KeyRowsAsync()).Select(row => row.CustomerId).Order(StringComparer.Ordinal));
        Assert.Equal(2, (await factory.CountRowsAsync()).Disputes);
    }

    [Theory]
    [InlineData(0)]      // expires exactly now
    [InlineData(-3600)]  // expired an hour ago
    public async Task Raise_UnderAnExpiredKey_ReplacesTheRowAndRunsAfresh(int expiredSecondsAgo)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();
        await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);
        await factory.SetExpiresAtAsync(stub.Now.AddSeconds(expiredSecondsAgo));

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body("TXN-A-0028"), key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        IdempotencyKey row = Assert.Single(await factory.KeyRowsAsync());
        Assert.Equal(stub.Now.AddHours(24).UtcTicks / 10, row.ExpiresAtUtc.UtcTicks / 10);
        Assert.Equal(2, (await factory.CountRowsAsync()).Disputes);
    }

    [Fact]
    public async Task Raise_UnderAKeyOneMicrosecondFromExpiring_IsStillAMismatch()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();
        await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);
        await factory.SetExpiresAtAsync(stub.Now.AddTicks(10));

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body("TXN-A-0028"), key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Raise_FirstUse_KeepsTheKeyFor24Hours()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        await browser.RaiseAsync(DisputesApi.Body());

        IdempotencyKey row = Assert.Single(await factory.KeyRowsAsync());
        Assert.Equal(TimeSpan.FromHours(24), row.ExpiresAtUtc - row.CreatedAtUtc);
    }

    [Fact]
    public async Task Raise_LeaseLostBeforeTheCommit_RollsTheDisputeBackAndLeavesTheNewHoldersKey()
    {
        var takeOver = new TakeOverLeaseInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(takeOver)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(), DisputesApi.NewKey());

        Assert.Equal(1, takeOver.TakenOver);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
        // The new holder's row is neither completed by the old lease nor released by it.
        IdempotencyKey row = Assert.Single(await factory.KeyRowsAsync());
        Assert.Equal(IdempotencyKeyStatus.InProgress, row.Status);
        Assert.Null(row.ResponseBody);
    }

    [Fact]
    public async Task Raise_5xxAfterTheRaiseCommitted_LeavesTheCompletedKeySoARetryReplaysThe201()
    {
        var failAfterCommit = new FailAfterCommitInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAfterCommit)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();

        HttpResponseMessage failed = await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);
        HttpResponseMessage retry = await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);

        Assert.Equal(1, failAfterCommit.Failed);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        IdempotencyKey row = Assert.Single(await factory.KeyRowsAsync());
        Assert.Equal(IdempotencyKeyStatus.Completed, row.Status);
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }
}
