using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Operations;
using DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// A record's generated <c>ToString</c> prints every property. The operations response carries the transaction snapshot
/// and the resolve note, free text that may hold personal data, so each record prints only its type name and seals it
/// (OWASP Logging Cheat Sheet: data to exclude; ASVS 5.0 V16.2.5). Every field is set, so the exact match covers each one.
/// </summary>
public class OperationsResponsePrivacyTests
{
    [Fact]
    public void OperationsTimelineEntryResponse_ToString_PrintsOnlyTheTypeNameAndIsSealed()
    {
        OperationsTimelineEntryResponse entry = Resolved();

        Assert.Equal(nameof(OperationsTimelineEntryResponse), entry.ToString());
        Assert.True(typeof(OperationsTimelineEntryResponse).GetMethod(nameof(ToString), Type.EmptyTypes)?.IsFinal);
    }

    [Fact]
    public void OperationsDisputeResponse_ToString_PrintsOnlyTheTypeNameAndIsSealed()
    {
        var transaction = new TransactionSnapshotResponse(
            "transaction-marker", "account-marker", "last4-marker", DateTimeOffset.UnixEpoch, "description-marker",
            new Money("87.23", "ZAR"), "card-marker");
        var dispute = new OperationsDisputeResponse(
            Guid.Empty, DisputeStatus.Resolved, Outcome.Upheld, Reason.WrongAmount, transaction, new Money("80.00", "ZAR"),
            new Money("7.23", "ZAR"), DateTimeOffset.UnixEpoch, [Resolved()]);

        Assert.Equal(nameof(OperationsDisputeResponse), dispute.ToString());
        Assert.True(typeof(OperationsDisputeResponse).GetMethod(nameof(ToString), Type.EmptyTypes)?.IsFinal);
    }

    private static OperationsTimelineEntryResponse Resolved() => new(
        StoredEventType.DisputeResolved, ActorType.Operations, DateTimeOffset.UnixEpoch, DisputeStatus.Escalated,
        DisputeStatus.Resolved, "resolve-note-marker");
}
