using DisputeDesk.Application.Operations;
using DisputeDesk.Contracts.Operations;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AD-13: the operations timeline maps every stored event type, and throws on one it does not know. No database: every
/// concrete <see cref="StoredEvent"/> type in the Domain has a sample here, so a type added later fails this test in CI
/// until it has a sample and an <see cref="OperationsView"/> mapping, rather than failing a read.
/// </summary>
public class OperationsViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static readonly StoredEvent[] Samples =
    [
        new DisputeRaised("CUS-000001", Transaction(), Reason.Unrecognised, "Not me", null, 10m, DisputeStatus.Submitted),
        new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview),
        new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated),
        new DisputeResolved(DisputeStatus.Escalated, DisputeStatus.Resolved, Outcome.Upheld, "Refunded"),
        new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, "Found it"),
        new SlaWarningRaised(),
    ];

    [Fact]
    public void Samples_CoverEveryConcreteStoredEventType()
    {
        IEnumerable<string> domainTypes = typeof(StoredEvent).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(StoredEvent)) && !type.IsAbstract)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(domainTypes, Samples.Select(sample => sample.GetType().Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ToResponse_EveryStoredEventType_GivesOneEntryEach()
    {
        StoredEvent[] stream =
        [
            .. Samples.Select((sample, index) =>
                sample with { Version = index + 1, OccurredAtUtc = Now.AddHours(index), ActorType = ActorType.Operations, ActorId = "actor" }),
        ];
        // The dispute's own fields come from its raise; the timeline is every stored event given.
        var dispute = Dispute.Replay([stream[0]]);

        OperationsDisputeResponse response = OperationsView.ToResponse(dispute, stream);

        Assert.Equal(
            [.. stream.Select(storedEvent => storedEvent.OccurredAtUtc)],
            response.Timeline.Select(entry => entry.OccurredAt));
    }

    private static TransactionSnapshot Transaction() => new(
        "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Shop", 10m, "ZAR", null,
        TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
}
