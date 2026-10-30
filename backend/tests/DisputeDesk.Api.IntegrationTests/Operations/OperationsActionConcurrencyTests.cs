using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC4 (AD-15; EF Core, "Handling concurrency conflicts"): a version clash is only a save refused by
/// <c>UQ_StoredEvents_DisputeId_Version</c>. The action then clears the change tracker, replays through the named bypass
/// and decides once more with the same request; a second clash answers 409 with the status as last read, with no further
/// attempt. Each 409 is audited as <c>Rejected</c>.
/// </summary>
public class OperationsActionConcurrencyTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    // The replay of both the operations actions and the customer's withdraw; never the raise's or the test's own queries.
    private const string ReplayRead = "ORDER BY s.\"Version\"";

    [Fact]
    public async Task Resolve_TwoAtOnceOnUnderReview_OneSucceedsAndTheOtherReloadsAndGets409Resolved()
    {
        var rendezvous = new RendezvousInterceptor(ReplayRead, parties: 2);
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(rendezvous)));
        Guid disputeId = await UnderReviewAsync(factory);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage[] responses = await Task.WhenAll(
            factory.ActAsMappedAsync(tool, disputeId, "resolve", OperationsApi.ResolveBody("upheld")),
            factory.ActAsMappedAsync(tool, disputeId, "resolve", OperationsApi.ResolveBody("rejected")));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        await responses.Single(response => response.StatusCode == HttpStatusCode.Conflict).AssertNotAllowedInAsync("resolved");
        Assert.Single(await factory.StreamAsync(disputeId), row => row.EventType == nameof(DisputeResolved));
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Resolved, row.Status);
        Assert.Equal(3, row.Version);
        AssertOneRejected(await factory.DisputeAuditRowsAsync(), AuditAction.Resolve, disputeId);
    }

    [Fact]
    public async Task Resolve_AgainstTheCustomersWithdrawOnUnderReview_ExactlyOneSucceedsAndTheOtherGets409WithTheReloadedStatus()
    {
        var rendezvous = new RendezvousInterceptor(ReplayRead, parties: 2);
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(rendezvous)));
        Guid disputeId = await UnderReviewAsync(factory);
        // RaiseForCustomerAAsync linked login-a already, so this only signs in.
        using HttpClient browser = factory.CreateBrowser(new System.Net.CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-a");
        string antiforgery = await browser.AntiforgeryTokenAsync();
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage[] responses = await Task.WhenAll(
            factory.ActAsMappedAsync(tool, disputeId, "resolve"),
            browser.WithdrawAsync(disputeId.ToString(), "{}", antiforgery));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(3, row.Version);
        if (responses[0].StatusCode == HttpStatusCode.Conflict)
        {
            // The resolve lost: operations see the internal status, and nothing of the customer.
            Assert.Equal(DisputeStatus.Withdrawn, row.Status);
            await responses[0].AssertNotAllowedInAsync("withdrawn");
            Assert.DoesNotContain(TransactionsApi.CustomerA, await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
            AssertOneRejected(await factory.DisputeAuditRowsAsync(), AuditAction.Resolve, disputeId);
        }
        else
        {
            Assert.Equal(DisputeStatus.Resolved, row.Status);
            await responses[1].AssertActionNotAllowedAsync("resolved");
        }
    }

    [Fact]
    public async Task Escalate_ReviewStartedCommittedAhead_SucceedsOnItsOneRetryFromUnderReviewAtTheStreamsLatestInstant()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        // The review starts 5 minutes after the escalate's own instant, so the retry must take the stream's later one.
        DateTimeOffset reviewAt = stub.NowAsStored.AddMinutes(5);
        appendFirst.Arm(Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2, reviewAt), DisputeStatus.UnderReview);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await factory.ActAsMappedAsync(tool, disputeId, "escalate");

        Assert.Equal(1, appendFirst.Appended);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement last = body.RootElement.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("disputeEscalated", last.GetProperty("eventType").GetString());
        Assert.Equal("underReview", last.GetProperty("fromStatus").GetString());
        Assert.Equal(reviewAt, last.GetProperty("occurredAt").GetDateTimeOffset());
        List<StoredEventRow> stream = await factory.StreamAsync(disputeId);
        DisputeEscalated escalated = Assert.IsType<DisputeEscalated>(stream[2].ToStoredEvent());
        Assert.Equal(DisputeStatus.UnderReview, escalated.FromStatus);
        Assert.Equal(3, escalated.Version);
        Assert.Equal(reviewAt, escalated.OccurredAtUtc);
        using var payload = JsonDocument.Parse((await factory.OutboxRowsAsync(disputeId))[^1].Payload);
        Assert.Equal("underReview", payload.RootElement.GetProperty("data").GetProperty("fromStatus").GetString());
        Assert.Equal(DisputeStatus.Escalated, (await factory.DisputeRowAsync(disputeId)).Status);
        Assert.Empty(await factory.DisputeAuditRowsAsync());
    }

    [Fact]
    public async Task StartReview_DisputeEscalatedCommittedAhead_Gets409EscalatedOnItsRetry()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        appendFirst.Arm(Next(new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated), disputeId, 2, stub.NowAsStored), DisputeStatus.Escalated);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await factory.ActAsMappedAsync(tool, disputeId, "start-review");

        Assert.Equal(1, appendFirst.Appended);
        await response.AssertNotAllowedInAsync("escalated");
        Assert.Equal([nameof(DisputeRaised), nameof(DisputeEscalated)], (await factory.StreamAsync(disputeId)).Select(row => row.EventType));
        AssertOneRejected(await factory.DisputeAuditRowsAsync(), AuditAction.StartReview, disputeId);
    }

    [Fact]
    public async Task Resolve_TwoClashes_Answers409WithTheStatusAsLastReadAndNeverRetriesAgain()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        Guid disputeId = await UnderReviewAsync(factory);
        appendFirst.Arm(Next(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), disputeId, 3, stub.NowAsStored), DisputeStatus.Escalated);
        // The second clash leaves the dispute escalated, which resolve allows: a third replay-and-decide would commit
        // version 5 and answer 200, so only the read of the Disputes row gives this 409.
        appendFirst.Arm(Next(new DisputeEscalated(DisputeStatus.Escalated, DisputeStatus.Escalated), disputeId, 4, stub.NowAsStored), DisputeStatus.Escalated);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await factory.ActAsMappedAsync(tool, disputeId, "resolve");

        Assert.Equal(2, appendFirst.Appended);
        await response.AssertNotAllowedInAsync("escalated");
        List<StoredEventRow> stream = await factory.StreamAsync(disputeId);
        Assert.Equal(
            [nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeEscalated), nameof(DisputeEscalated)],
            stream.Select(row => row.EventType));
        Assert.Equal(4, stream[^1].Version);
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Escalated, row.Status);
        Assert.Equal(4, row.Version);
        // The audit save after the second clash ran on the cleared change tracker.
        AssertOneRejected(await factory.DisputeAuditRowsAsync(), AuditAction.Resolve, disputeId);
    }

    /// <summary>
    /// A dispute of customer A put under review by a stored event written directly, so no replay read runs before the
    /// test's own requests (the rendezvous holds the first two).
    /// </summary>
    private async Task<Guid> UnderReviewAsync(ApiFactory factory)
    {
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        await factory.AppendAsync(
            disputeId, DisputeStatus.UnderReview, null, Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2, stub.NowAsStored));
        return disputeId;
    }

    private void AssertOneRejected(List<AuditLogEntry> rows, AuditAction action, Guid disputeId)
    {
        AuditLogEntry entry = Assert.Single(rows);
        Assert.Equal(action, entry.Action);
        Assert.Equal(AuditResult.Rejected, entry.Result);
        Assert.Equal(ActorType.Operations, entry.ActorType);
        Assert.Equal(FakeZitadel.OperationsSub, entry.ActorId);
        Assert.Equal(disputeId, entry.DisputeId);
        Assert.Null(entry.CustomerId);
        Assert.Equal(stub.NowAsStored, entry.OccurredAtUtc);
    }

    private static TEvent Next<TEvent>(TEvent storedEvent, Guid disputeId, int version, DateTimeOffset at)
        where TEvent : StoredEvent =>
        storedEvent with { DisputeId = disputeId, Version = version, OccurredAtUtc = at, ActorType = ActorType.Operations, ActorId = "ops-b" };
}
