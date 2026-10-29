using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes.RaiseDispute;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;

/// <summary>
/// AC2: the same validated request under a completed key gets the stored 201 again, byte for byte with its
/// <c>Location</c>, and writes no second dispute, timeline entry, audit entry or outbox row.
/// </summary>
public class IdempotencyReplayTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Raise_SameRequestAndKeyAgain_ReplaysThe201ByteForByteAndWritesNothingMore()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();
        string body = DisputesApi.Body(note: "I never bought this");

        HttpResponseMessage original = await browser.RaiseWithKeyAsync(body, key);
        HttpResponseMessage replay = await browser.RaiseWithKeyAsync(body, key);

        Assert.Equal(HttpStatusCode.Created, original.StatusCode);
        Assert.NotNull(original.Headers.Location);
        await IdempotencyRows.AssertSameResponseAsync(original, replay);
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
        IdempotencyKey stored = Assert.Single(await factory.KeyRowsAsync());
        Assert.Equal(IdempotencyKeyStatus.Completed, stored.Status);
        Assert.Equal(201, stored.ResponseStatus);
    }

    [Fact]
    public async Task Raise_SameRequestWithPropertiesReorderedAndAnUnknownOne_IsTheSameRequestAndReplays()
    {
        // The hash is over the validated request re-serialised, so property order and ignored properties do not count.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();

        HttpResponseMessage original = await browser.RaiseWithKeyAsync(
            """{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":"wrongAmount","expectedAmount":"80.00"}""", key);
        HttpResponseMessage replay = await browser.RaiseWithKeyAsync(
            """{ "expectedAmount": "80.00", "reason": "wrongAmount", "colour": "blue", "transactionId": "TXN-A-0029", "accountRef": "ACC-A-1" }""", key);

        Assert.Equal(HttpStatusCode.Created, original.StatusCode);
        await IdempotencyRows.AssertSameResponseAsync(original, replay);
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_DoubleSubmitOfOneKey_CreatesExactlyOneDispute()
    {
        // A double click: the same request and key five times at once. Each gets the 201, its replay, or 409 in progress.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string key = DisputesApi.NewKey();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => browser.RaiseAsync(DisputesApi.Body(), token, key)));

        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        byte[][] created = await Task.WhenAll(
            responses.Where(response => response.StatusCode == HttpStatusCode.Created).Select(response => response.BodyBytesAsync()));
        Assert.NotEmpty(created);
        Assert.All(created, bytes => Assert.Equal(created[0], bytes));
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }
}
