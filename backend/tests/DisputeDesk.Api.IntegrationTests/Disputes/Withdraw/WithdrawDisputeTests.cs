using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Events;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Withdraw;

/// <summary>
/// AC1, AC3 and AC6: the owner withdraws an open dispute by replay, one commit writes the stored event, its outbox row
/// and the read model, the 200 is the detail, closed disputes refuse with 409 and Withdrawn is final.
/// </summary>
public class WithdrawDisputeTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Withdraw_OwnSubmittedDispute_AppendsDisputeWithdrawnAndTheRowShowsWithdrawnAtVersion2()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = await RaisedAsync(browser);

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId.ToString(), DisputesApi.WithdrawBody("  Found it in my statement  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<StoredEventRow> stream = await factory.StreamAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(DisputeWithdrawn)], stream.Select(row => row.EventType));
        StoredEventRow withdrawn = stream[1];
        Assert.Equal(2, withdrawn.Version);
        Assert.Equal(ActorType.Customer, withdrawn.ActorType);
        Assert.Equal("login-a", withdrawn.ActorId);
        Assert.Equal(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, "Found it in my statement") with
        {
            DisputeId = disputeId,
            Version = 2,
            OccurredAtUtc = stub.NowAsStored,
            ActorType = ActorType.Customer,
            ActorId = "login-a",
            TraceId = withdrawn.TraceId,
        }, withdrawn.ToStoredEvent());
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Withdrawn, row.Status);
        Assert.Equal(2, row.Version);
        // A success is a stored event, never an audit row (ad-009).
        Assert.Empty(await factory.DisputeAuditRowsAsync());
    }

    [Fact]
    public async Task Withdraw_Succeeds_200IsTheFollowingDetailWithTheWithdrawAsTheLastTimelineItem()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body(note: "Not me"))).GetProperty("disputeId").GetString() ?? "";

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId, DisputesApi.WithdrawBody("  Found it  "));
        string withdrawBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        HttpResponseMessage detail = await browser.GetDisputeAsync(disputeId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await detail.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), withdrawBody);
        using var body = JsonDocument.Parse(withdrawBody);
        Assert.Equal("withdrawn", body.RootElement.GetProperty("customerStatus").GetString());
        Assert.Equal("Not me", body.RootElement.GetProperty("note").GetString());
        JsonElement last = body.RootElement.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("withdraw", last.GetProperty("action").GetString());
        Assert.Equal("withdrawn", last.GetProperty("customerStatus").GetString());
        Assert.Equal("you", last.GetProperty("actor").GetString());
        Assert.Equal("Found it", last.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Withdraw_ClockNotMicrosecondAligned_200DetailAndOutboxHoldTheSameStoredInstant()
    {
        // The stub's clock reads .1234567 s, a tick finer than Postgres keeps (TransactionsStub).
        Assert.NotEqual(stub.Now, stub.NowAsStored);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = await RaisedAsync(browser);

        JsonElement withdrawn = await browser.WithdrawOkAsync(disputeId.ToString());
        using JsonDocument detail = await (await browser.GetDisputeAsync(disputeId.ToString())).ReadJsonAsync();

        Assert.Equal(stub.NowAsStored, LastOccurredAt(withdrawn));
        Assert.Equal(stub.NowAsStored, LastOccurredAt(detail.RootElement));
        OutboxMessage outbox = (await factory.OutboxRowsAsync(disputeId))[^1];
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(stub.NowAsStored, payload.RootElement.GetProperty("data").GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(stub.NowAsStored, payload.RootElement.GetProperty("time").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(DisputeStatus.UnderReview, "underReview")]
    [InlineData(DisputeStatus.Escalated, "escalated")]
    public async Task Withdraw_UnderReviewOrEscalated_SucceedsFromThatStatus(DisputeStatus status, string fromStatus)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = await RaisedAsync(browser);
        await factory.AppendAsync(disputeId, status, outcome: null, StatusChange(status, version: 2));

        JsonElement withdrawn = await browser.WithdrawOkAsync(disputeId.ToString());

        Assert.Equal("withdrawn", withdrawn.GetProperty("customerStatus").GetString());
        Assert.Equal(3, (await factory.DisputeRowAsync(disputeId)).Version);
        using var payload = JsonDocument.Parse((await factory.OutboxRowsAsync(disputeId))[^1].Payload);
        Assert.Equal(fromStatus, payload.RootElement.GetProperty("data").GetProperty("fromStatus").GetString());
    }

    [Fact]
    public async Task Withdraw_Succeeds_OutboxRowIsTheStatusChangedEventOfTheStoredEventWithNoNote()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = await RaisedAsync(browser);

        await browser.WithdrawOkAsync(disputeId.ToString(), DisputesApi.WithdrawBody("withdraw-note-marker"));

        List<OutboxMessage> outbox = await factory.OutboxRowsAsync(disputeId);
        Assert.Equal(2, outbox.Count);
        OutboxMessage message = outbox[1];
        StoredEventRow stored = (await factory.StreamAsync(disputeId))[1];
        Assert.Equal(stored.Id, message.Id);
        Assert.Equal(DisputeEvents.StatusChangedV1, message.EventType);
        Assert.Equal(2, message.DisputeVersion);
        Assert.DoesNotContain("withdraw-note-marker", message.Payload, StringComparison.Ordinal);
        using var payload = JsonDocument.Parse(message.Payload);
        JsonElement cloudEvent = payload.RootElement;
        Assert.Equal(
            ["specversion", "id", "source", "type", "subject", "time", "datacontenttype", "dataschema", "data"],
            cloudEvent.EnumerateObject().Select(property => property.Name));
        Assert.Equal("1.0", cloudEvent.GetProperty("specversion").GetString());
        Assert.Equal(stored.Id.ToString(), cloudEvent.GetProperty("id").GetString());
        Assert.Equal("/disputedesk/api", cloudEvent.GetProperty("source").GetString());
        Assert.Equal(DisputeEvents.StatusChangedV1, cloudEvent.GetProperty("type").GetString());
        Assert.Equal(disputeId.ToString(), cloudEvent.GetProperty("subject").GetString());
        Assert.Equal("application/json", cloudEvent.GetProperty("datacontenttype").GetString());
        Assert.Equal(DisputeEvents.SchemaFor(DisputeEvents.StatusChangedV1), cloudEvent.GetProperty("dataschema").GetString());
        JsonElement data = cloudEvent.GetProperty("data");
        Assert.Equal(
            ["disputeId", "fromStatus", "toStatus", "action", "actorType", "occurredAt", "disputeVersion"],
            data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(disputeId.ToString(), data.GetProperty("disputeId").GetString());
        Assert.Equal("submitted", data.GetProperty("fromStatus").GetString());
        Assert.Equal("withdrawn", data.GetProperty("toStatus").GetString());
        Assert.Equal("withdraw", data.GetProperty("action").GetString());
        Assert.Equal("customer", data.GetProperty("actorType").GetString());
        Assert.Equal(2, data.GetProperty("disputeVersion").GetInt32());
        PublishMappingTests.AssertValid(message.Payload);
    }

    [Theory]
    [InlineData(DisputeStatus.Resolved, "resolved")]
    [InlineData(DisputeStatus.Withdrawn, "withdrawn")]
    public async Task Withdraw_ClosedDispute_Returns409ActionNotAllowedWithTheCustomerStatusAndWritesNoEvent(DisputeStatus status, string customerStatus)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = await RaisedAsync(browser);
        await factory.AppendAsync(disputeId, status, status == DisputeStatus.Resolved ? Outcome.Rejected : null, StatusChange(status, version: 2));

        HttpResponseMessage response = await browser.WithdrawAsync(disputeId.ToString());

        await response.AssertActionNotAllowedAsync(customerStatus);
        Assert.Equal(2, (await factory.StreamAsync(disputeId)).Count);
        Assert.Single(await factory.OutboxRowsAsync(disputeId));
    }

    [Fact]
    public async Task Withdraw_TwiceInARow_SecondGets409WithdrawnAndTheStreamHoldsOneDisputeWithdrawn()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = await RaisedAsync(browser);
        await browser.WithdrawOkAsync(disputeId.ToString());

        HttpResponseMessage second = await browser.WithdrawAsync(disputeId.ToString());

        await second.AssertActionNotAllowedAsync("withdrawn");
        Assert.Single(await factory.StreamAsync(disputeId), row => row.EventType == nameof(DisputeWithdrawn));
        Assert.Single(await factory.OutboxRowsAsync(disputeId), message => message.EventType == DisputeEvents.StatusChangedV1);
    }

    [Fact]
    public async Task Raise_WithdrawnDisputesTransactionAgain_Returns409AlreadyDisputedAndTheRowStaysNotDisputable()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = await RaisedAsync(browser);
        await browser.WithdrawOkAsync(disputeId.ToString());

        HttpResponseMessage raise = await browser.RaiseAsync(DisputesApi.Body());

        await raise.AssertAlreadyDisputedAsync(disputeId.ToString(), "withdrawn");
        HttpResponseMessage list = await browser.GetPathAsync("/api/v1/transactions?limit=50");
        using JsonDocument page = await list.ReadJsonAsync();
        JsonElement row = page.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("transactionId").GetString() == DisputesApi.Disputable);
        Assert.False(row.GetProperty("disputable").GetBoolean());
        Assert.Equal("alreadyDisputed", row.GetProperty("notDisputableReason").GetString());
        Assert.Equal("withdrawn", row.GetProperty("customerStatus").GetString());
    }

    private static async Task<Guid> RaisedAsync(HttpClient browser) =>
        (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();

    private static DateTimeOffset LastOccurredAt(JsonElement dispute) =>
        dispute.GetProperty("timeline").EnumerateArray().Last().GetProperty("occurredAt").GetDateTimeOffset();

    /// <summary>The stored event that moves a just-raised dispute to <paramref name="status"/>, as the operations actions or a withdraw would.</summary>
    private StoredEvent StatusChange(DisputeStatus status, int version)
    {
        StoredEvent storedEvent = status switch
        {
            DisputeStatus.UnderReview => new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview),
            DisputeStatus.Escalated => new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated),
            DisputeStatus.Resolved => new DisputeResolved(DisputeStatus.Submitted, DisputeStatus.Resolved, Outcome.Rejected, "Checked"),
            DisputeStatus.Withdrawn => new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No stored event leads there."),
        };
        return storedEvent with
        {
            Version = version,
            OccurredAtUtc = stub.NowAsStored,
            ActorType = status == DisputeStatus.Withdrawn ? ActorType.Customer : ActorType.Operations,
            ActorId = "actor-" + version,
        };
    }
}
