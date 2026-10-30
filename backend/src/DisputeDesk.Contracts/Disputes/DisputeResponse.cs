using DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// A dispute as its customer sees it (AD-13): no actor identity, SLA or due date. <c>outcome</c> is <c>upheld</c> or
/// <c>rejected</c> once the dispute is resolved, and null until then. <c>expectedAmount</c> is set only for the reason
/// <c>wrongAmount</c>; <c>disputedAmount</c> is then the transaction amount minus it.
/// </summary>
public sealed record DisputeResponse(
    Guid DisputeId,
    CustomerStatus CustomerStatus,
    Outcome? Outcome,
    Reason Reason,
    string? Note,
    TransactionSnapshotResponse Transaction,
    Money? ExpectedAmount,
    Money DisputedAmount,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TimelineEntryResponse> Timeline)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the customer's
    /// note, free text that may hold personal data, and the transaction snapshot (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(DisputeResponse);
}
