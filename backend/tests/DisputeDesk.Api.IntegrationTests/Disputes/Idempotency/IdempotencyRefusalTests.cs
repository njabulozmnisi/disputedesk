using System.Net;
using DisputeDesk.Api.Disputes.Idempotency;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;

/// <summary>
/// AC4: every 4xx the handler returns (404, 409 <c>already-disputed</c>, 422 <c>not-disputable</c> and the
/// expected-amount 400) is stored under the key and replayed byte for byte, the original <c>traceId</c> included; a 503
/// releases the key, so a retry runs afresh.
/// </summary>
public class IdempotencyRefusalTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Theory]
    [InlineData("ACC-A-1", "TXN-A-9999", "unrecognised", null, 404)]                    // not found
    [InlineData("ACC-A-1", DisputesApi.Pending, "unrecognised", null, 422)]              // not disputable
    [InlineData("ACC-A-1", DisputesApi.Disputable, "wrongAmount", "100.00", 400)]        // expected amount not below
    public async Task Raise_HandlerRefusal_IsStoredAndReplayedByteForByte(
        string accountRef, string transactionId, string reason, string? expectedAmount, int status)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();
        string body = DisputesApi.Body(transactionId, accountRef, reason, expectedAmount: expectedAmount);

        HttpResponseMessage original = await browser.RaiseWithKeyAsync(body, key);
        HttpResponseMessage replay = await browser.RaiseWithKeyAsync(body, key);

        Assert.Equal(status, (int)original.StatusCode);
        // Each request has its own traceId, so equal bytes mean the replay is the stored copy, not a second run.
        await IdempotencyRows.AssertSameResponseAsync(original, replay);
        IdempotencyKey stored = Assert.Single(await factory.KeyRowsAsync());
        Assert.Equal(IdempotencyKeyStatus.Completed, stored.Status);
        Assert.Equal(status, stored.ResponseStatus);
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_AlreadyDisputed_409IsStoredAndReplayedEvenAfterTheDisputeChanges()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Assert.Equal(HttpStatusCode.Created, (await browser.RaiseAsync(DisputesApi.Body())).StatusCode);
        string key = DisputesApi.NewKey();

        HttpResponseMessage original = await browser.RaiseWithKeyAsync(DisputesApi.Body(reason: "duplicateCharge"), key);
        await factory.SetStatusAsync(Domain.Disputes.DisputeStatus.Resolved);
        HttpResponseMessage replay = await browser.RaiseWithKeyAsync(DisputesApi.Body(reason: "duplicateCharge"), key);

        Assert.Equal(HttpStatusCode.Conflict, original.StatusCode);
        await IdempotencyRows.AssertSameResponseAsync(original, replay);
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_LosingTheRaceToTheUniqueIndex_Stores409ForTheLosersKey()
    {
        // The loser's save fails and the context forgets its rows (ChangeTracker.Clear); the 409 is then stored with a
        // lease-conditional update, which does not depend on what the context tracks.
        var rendezvous = new RendezvousInterceptor("FROM \"Disputes\"", parties: 2);
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(rendezvous)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string[] keys = [DisputesApi.NewKey(), DisputesApi.NewKey()];

        HttpResponseMessage[] responses = await Task.WhenAll(keys.Select(key => browser.RaiseAsync(DisputesApi.Body(), token, key)));

        int loser = Array.FindIndex(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.NotEqual(-1, loser);
        await IdempotencyRows.AssertSameResponseAsync(responses[loser], await browser.RaiseAsync(DisputesApi.Body(), token, keys[loser]));
        Assert.Equal([201, 409], (await factory.KeyRowsAsync()).Select(row => row.ResponseStatus ?? 0).Order());
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_CoreBankingUnavailable_503ReleasesTheKeySoARetryRunsAfresh()
    {
        await using FakeCoreBankingServer failing = await FakeCoreBankingServer.FailingAsync();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, failing.BaseUrl, stub.Now);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();

        HttpResponseMessage first = await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);
        Assert.Empty(await factory.KeyRowsAsync());
        int requestsBeforeRetry = failing.RequestCount;
        HttpResponseMessage retry = await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, retry.StatusCode);
        Assert.True(failing.RequestCount > requestsBeforeRetry, "The retry should have asked core banking again.");
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_OutboxInsertFails_500ReleasesTheKey()
    {
        var failOutbox = new FailOutboxInsertInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failOutbox)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(), DisputesApi.NewKey());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await factory.KeyRowsAsync());
    }

    [Fact]
    public async Task Filter_EndpointReturnsAnUnexpectedResult_ThrowsAndReleasesTheKey()
    {
        // The raise only returns the stored 201 or a problem; anything else is a programming error, so it fails as a 500.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentCustomer>().Set(TransactionsApi.CustomerA, "login-a");
        IdempotencyFilter filter = ActivatorUtilities.CreateInstance<IdempotencyFilter>(scope.ServiceProvider);
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Headers[IdempotencyKeyHeader.Name] = DisputesApi.NewKey();
        var context = new DefaultEndpointFilterInvocationContext(
            http, new RaiseDisputeRequest("ACC-A-1", DisputesApi.Disputable, Reason.Unrecognised));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(TypedResults.Ok())));

        Assert.Empty(await factory.KeyRowsAsync());
    }
}
