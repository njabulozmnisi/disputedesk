using System.Text.Json;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// Story 8.17 AC5 (D14b): every persisted and published shape is byte-identical for fixed inputs. Each case serialises
/// through the production code (the publish mapping, the stored event row, the raise 201 the idempotency key stores)
/// and compares the whole string, so a change to any enum on the wire, a property name or the field order fails here.
/// </summary>
public class WireBytesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 41, 12, TimeSpan.Zero);
    private static readonly Guid DisputeId = new("01990000-0000-7000-8000-00000000000d");
    private static readonly Guid EventId = new("01990000-0000-7000-8000-00000000000e");

    private const string Envelope =
        "{\"specversion\":\"1.0\",\"id\":\"01990000-0000-7000-8000-00000000000e\",\"source\":\"/disputedesk/api\",\"type\":\"{type}\","
        + "\"subject\":\"01990000-0000-7000-8000-00000000000d\",\"time\":\"2026-10-30T09:41:12Z\",\"datacontenttype\":\"application/json\","
        + "\"dataschema\":\"https://github.com/njabulozmnisi/disputedesk/blob/main/contracts/events/{type}.schema.json\",\"data\":{data}}";

    /// <summary>One stored event of each published type, with the exact <c>data</c> of its CloudEvent.</summary>
    public static TheoryData<StoredEvent, string, string> PublishedPayloads => new()
    {
        {
            Raised(), "disputedesk.dispute.raised.v1",
            "{\"disputeId\":\"01990000-0000-7000-8000-00000000000d\",\"reason\":\"wrongAmount\",\"status\":\"submitted\",\"occurredAt\":\"2026-10-30T09:41:12Z\",\"disputeVersion\":1}"
        },
        {
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Operations), "disputedesk.dispute.status-changed.v1",
            "{\"disputeId\":\"01990000-0000-7000-8000-00000000000d\",\"fromStatus\":\"submitted\",\"toStatus\":\"underReview\",\"action\":\"startReview\",\"actorType\":\"operations\",\"occurredAt\":\"2026-10-30T09:41:12Z\",\"disputeVersion\":2}"
        },
        {
            At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 3, ActorType.Automation), "disputedesk.dispute.status-changed.v1",
            "{\"disputeId\":\"01990000-0000-7000-8000-00000000000d\",\"fromStatus\":\"underReview\",\"toStatus\":\"escalated\",\"action\":\"escalate\",\"actorType\":\"automation\",\"occurredAt\":\"2026-10-30T09:41:12Z\",\"disputeVersion\":3}"
        },
        {
            At(new DisputeResolved(DisputeStatus.Escalated, DisputeStatus.Resolved, Outcome.Upheld, "Refund agreed."), 4, ActorType.Operations), "disputedesk.dispute.status-changed.v1",
            "{\"disputeId\":\"01990000-0000-7000-8000-00000000000d\",\"fromStatus\":\"escalated\",\"toStatus\":\"resolved\",\"action\":\"resolve\",\"actorType\":\"operations\",\"outcome\":\"upheld\",\"occurredAt\":\"2026-10-30T09:41:12Z\",\"disputeVersion\":4}"
        },
        {
            At(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, "Found it."), 2, ActorType.Customer), "disputedesk.dispute.status-changed.v1",
            "{\"disputeId\":\"01990000-0000-7000-8000-00000000000d\",\"fromStatus\":\"submitted\",\"toStatus\":\"withdrawn\",\"action\":\"withdraw\",\"actorType\":\"customer\",\"occurredAt\":\"2026-10-30T09:41:12Z\",\"disputeVersion\":2}"
        },
    };

    /// <summary>One stored event of each stored event type, with the exact <c>Data</c> its <c>StoredEvents</c> row holds.</summary>
    public static TheoryData<StoredEvent, string> StoredData => new()
    {
        {
            Raised(),
            "{\"customerId\":\"CUS-000001\",\"transaction\":{\"accountRef\":\"ACC-A-1\",\"transactionId\":\"TXN-A-0029\",\"accountLast4\":\"0011\","
            + "\"transactionDate\":\"2026-10-28T09:41:12+00:00\",\"description\":\"Steers Braamfontein\",\"amount\":87.23,\"currency\":\"ZAR\","
            + "\"maskedCard\":\"492181******4417\",\"direction\":\"debit\",\"kind\":\"card\",\"state\":\"settled\"},\"reason\":\"wrongAmount\","
            + "\"note\":\"Charged twice.\",\"expectedAmount\":80.00,\"disputedAmount\":7.23,\"toStatus\":\"submitted\"}"
        },
        { At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Operations), "{\"fromStatus\":\"submitted\",\"toStatus\":\"underReview\"}" },
        { At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 3, ActorType.Automation), "{\"fromStatus\":\"underReview\",\"toStatus\":\"escalated\"}" },
        {
            At(new DisputeResolved(DisputeStatus.Escalated, DisputeStatus.Resolved, Outcome.Rejected, "No error found."), 4, ActorType.Operations),
            "{\"fromStatus\":\"escalated\",\"toStatus\":\"resolved\",\"outcome\":\"rejected\",\"note\":\"No error found.\"}"
        },
        { At(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null), 2, ActorType.Customer), "{\"fromStatus\":\"submitted\",\"toStatus\":\"withdrawn\",\"note\":null}" },
    };

    [Fact]
    public void StoredData_CoversEveryStoredEventTypeInTheDomain()
    {
        IEnumerable<string> domainTypes = typeof(StoredEvent).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(StoredEvent))).Select(type => type.Name);

        Assert.Equal(
            domainTypes.Order(StringComparer.Ordinal),
            StoredData.Select(row => row.Data.Item1.GetType().Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(PublishedPayloads))]
    public void OutboxPayload_FixedInput_IsExactlyTheExpectedString(StoredEvent storedEvent, string type, string data)
    {
        string? payload = CommitRows.OutboxMessage(EventId, storedEvent, activity: null)?.Payload;

        Assert.Equal(Envelope.Replace("{type}", type, StringComparison.Ordinal).Replace("{data}", data, StringComparison.Ordinal), payload);
    }

    [Theory]
    [MemberData(nameof(StoredData))]
    public void StoredEventData_FixedInput_IsExactlyTheExpectedString(StoredEvent storedEvent, string data)
    {
        var row = StoredEventRow.From(EventId, Dispute.Replay([Raised()]), storedEvent, traceId: null);

        Assert.Equal(data, row.Data);
    }

    [Fact]
    public void StoredRaiseResponse_FixedInput_IsExactlyTheExpectedString()
    {
        // What RaiseDisputeHandler stores on the idempotency key: the 201 body of a new dispute and its one stored event.
        DisputeRaised raised = Raised();

        string stored = JsonSerializer.Serialize(CustomerView.ToResponse(Dispute.Replay([raised]), [raised]), DisputeDeskJson.Options);

        Assert.Equal(
            "{\"disputeId\":\"01990000-0000-7000-8000-00000000000d\",\"customerStatus\":\"submitted\",\"outcome\":null,\"reason\":\"wrongAmount\","
            + "\"note\":\"Charged twice.\",\"transaction\":{\"transactionId\":\"TXN-A-0029\",\"accountRef\":\"ACC-A-1\",\"accountLast4\":\"0011\","
            + "\"transactionDate\":\"2026-10-28T09:41:12+00:00\",\"description\":\"Steers Braamfontein\",\"amount\":{\"amount\":\"87.23\",\"currency\":\"ZAR\"},"
            + "\"maskedCard\":\"492181******4417\"},\"expectedAmount\":{\"amount\":\"80.00\",\"currency\":\"ZAR\"},"
            + "\"disputedAmount\":{\"amount\":\"7.23\",\"currency\":\"ZAR\"},\"createdAt\":\"2026-10-30T09:41:12+00:00\","
            + "\"timeline\":[{\"action\":\"raise\",\"customerStatus\":\"submitted\",\"actor\":\"you\",\"occurredAt\":\"2026-10-30T09:41:12+00:00\",\"note\":\"Charged twice.\"}]}",
            stored);
    }

    private static DisputeRaised Raised()
    {
        var transaction = new TransactionSnapshot(
            "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", "492181******4417",
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        return At(
            new DisputeRaised("CUS-000001", transaction, Reason.WrongAmount, "Charged twice.", 80.00m, 7.23m, DisputeStatus.Submitted),
            1, ActorType.Customer);
    }

    private static TEvent At<TEvent>(TEvent storedEvent, int version, ActorType actorType)
        where TEvent : StoredEvent =>
        storedEvent with { DisputeId = DisputeId, Version = version, OccurredAtUtc = Now, ActorType = actorType, ActorId = "actor" };
}
