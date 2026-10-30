using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Withdraw;

/// <summary>
/// AC5 (AD-15): a version clash is a save refused by <c>UQ_StoredEvents_DisputeId_Version</c>. The withdraw then replays
/// the stream once more and decides again; a second clash answers 409 with the status as last read, with no further
/// attempt.
/// </summary>
public class WithdrawConcurrencyTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Withdraw_TwoAtOnceBothPastTheReplay_OneSucceedsAndTheOtherReloadsAndGets409Withdrawn()
    {
        // Both requests' replay reads are held until both have read the stream, so both decide from version 1.
        var rendezvous = new RendezvousInterceptor("FROM \"StoredEvents\"", parties: 2);
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(rendezvous)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        string token = await browser.AntiforgeryTokenAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(
            browser.WithdrawAsync(disputeId.ToString(), "{}", token),
            browser.WithdrawAsync(disputeId.ToString(), "{}", token));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        await responses.Single(response => response.StatusCode == HttpStatusCode.Conflict).AssertActionNotAllowedAsync("withdrawn");
        Assert.Single(await factory.StreamAsync(disputeId), row => row.EventType == nameof(DisputeWithdrawn));
        Assert.Equal(2, (await factory.OutboxRowsAsync(disputeId)).Count);
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Withdrawn, row.Status);
        Assert.Equal(2, row.Version);
        await AssertOneRejectedWithdrawAsync(factory, disputeId);
    }

    [Fact]
    public async Task Withdraw_OneClash_SucceedsOnItsOneRetryFromUnderReviewAtTheStreamsLatestInstant()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        // The review starts 5 minutes after the withdraw's own instant, so the retry must take the stream's later one.
        DateTimeOffset reviewAt = stub.NowAsStored.AddMinutes(5);
        appendFirst.Arm(Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2, reviewAt), DisputeStatus.UnderReview);

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId.ToString());

        Assert.Equal(1, appendFirst.Appended);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement last = body.RootElement.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("withdraw", last.GetProperty("action").GetString());
        Assert.Equal(reviewAt, last.GetProperty("occurredAt").GetDateTimeOffset());
        List<StoredEventRow> stream = await factory.StreamAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeWithdrawn)], stream.Select(row => row.EventType));
        DisputeWithdrawn withdrawn = Assert.IsType<DisputeWithdrawn>(stream[2].ToStoredEvent());
        Assert.Equal(DisputeStatus.UnderReview, withdrawn.FromStatus);
        Assert.Equal(3, withdrawn.Version);
        Assert.Equal(reviewAt, withdrawn.OccurredAtUtc);
        using var payload = JsonDocument.Parse((await factory.OutboxRowsAsync(disputeId))[^1].Payload);
        Assert.Equal("underReview", payload.RootElement.GetProperty("data").GetProperty("fromStatus").GetString());
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Withdrawn, row.Status);
        Assert.Equal(3, row.Version);
    }

    [Fact]
    public async Task Withdraw_OneClashWithAnEarlierEvent_SucceedsOnItsOneRetryAtItsOwnInstant()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        // The other side of "the later of" (AD-15): the stream's latest instant is before the clock, so the retry keeps
        // the withdraw's own.
        Guid disputeId = await factory.RaiseDirectlyAsync("login-a", stub.NowAsStored.AddMinutes(-30));
        appendFirst.Arm(
            Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2, stub.NowAsStored.AddMinutes(-10)),
            DisputeStatus.UnderReview);

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId.ToString());

        Assert.Equal(1, appendFirst.Appended);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<StoredEventRow> stream = await factory.StreamAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeWithdrawn)], stream.Select(row => row.EventType));
        DisputeWithdrawn withdrawn = Assert.IsType<DisputeWithdrawn>(stream[2].ToStoredEvent());
        Assert.Equal(DisputeStatus.UnderReview, withdrawn.FromStatus);
        Assert.Equal(stub.NowAsStored, withdrawn.OccurredAtUtc);
    }

    [Fact]
    public async Task Withdraw_TwoClashes_Answers409WithTheStatusAsLastReadAndNeverRetriesAgain()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        appendFirst.Arm(Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2, stub.NowAsStored), DisputeStatus.UnderReview);
        appendFirst.Arm(Next(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), disputeId, 3, stub.NowAsStored), DisputeStatus.Escalated);

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId.ToString());

        Assert.Equal(2, appendFirst.Appended);
        await response.AssertActionNotAllowedAsync("underReviewPrioritised");
        // A third attempt would have committed: nothing was armed for it.
        Assert.Equal(
            [nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeEscalated)],
            (await factory.StreamAsync(disputeId)).Select(row => row.EventType));
        Assert.Equal(DisputeStatus.Escalated, (await factory.DisputeRowAsync(disputeId)).Status);
        Assert.Single(await factory.OutboxRowsAsync(disputeId));
        await AssertOneRejectedWithdrawAsync(factory, disputeId);
    }

    [Fact]
    public async Task Withdraw_EachClashOutcomeAndAStatusRefusal_IsAuditedAsRejectedForTheDispute()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        // A clash whose reload finds the dispute already withdrawn, by the customer's other device.
        appendFirst.Arm(
            Next(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null), disputeId, 2, stub.NowAsStored) with { ActorType = ActorType.Customer, ActorId = "login-a" },
            DisputeStatus.Withdrawn);

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId.ToString());

        await response.AssertActionNotAllowedAsync("withdrawn");
        await AssertOneRejectedWithdrawAsync(factory, disputeId);
    }

    /// <summary>The one dispute audit row: customer A's withdraw of <paramref name="disputeId"/>, Rejected at the clock (AD-14a).</summary>
    private async Task AssertOneRejectedWithdrawAsync(ApiFactory factory, Guid disputeId) =>
        Assert.Single(await factory.DisputeAuditRowsAsync()).AssertRejectedOfA(AuditAction.Withdraw, stub.NowAsStored, disputeId);

    private static TEvent Next<TEvent>(TEvent storedEvent, Guid disputeId, int version, DateTimeOffset at)
        where TEvent : StoredEvent =>
        storedEvent with { DisputeId = disputeId, Version = version, OccurredAtUtc = at, ActorType = ActorType.Operations, ActorId = "ops-a" };
}
