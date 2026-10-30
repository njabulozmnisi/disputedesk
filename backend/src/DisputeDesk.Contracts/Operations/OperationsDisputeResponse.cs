using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Contracts.Operations;

/// <summary>
/// A dispute as operations see it (FR-30): the internal status, the outcome (null until resolved), the reason, the
/// stored transaction snapshot, the amounts and the timeline of every stored event. No note the customer wrote, no
/// customer ID and no actor identity.
/// </summary>
public sealed record OperationsDisputeResponse(
    Guid DisputeId,
    DisputeStatus Status,
    Outcome? Outcome,
    Reason Reason,
    TransactionSnapshotResponse Transaction,
    Money? ExpectedAmount,
    Money DisputedAmount,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OperationsTimelineEntryResponse> Timeline)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the transaction
    /// snapshot and the timeline's resolve note, free text that may hold personal data (OWASP Logging Cheat Sheet: data to
    /// exclude). Sealed, so nothing overrides it again.
    /// </summary>
    public sealed override string ToString() => nameof(OperationsDisputeResponse);
}
