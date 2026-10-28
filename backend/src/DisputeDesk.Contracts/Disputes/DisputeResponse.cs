using DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// A dispute as its customer sees it (AD-13): no actor identity, SLA or due date. <c>expectedAmount</c> is set only
/// for the reason <c>wrongAmount</c>; <c>disputedAmount</c> is then the transaction amount minus it.
/// </summary>
public sealed record DisputeResponse(
    Guid DisputeId,
    CustomerStatus CustomerStatus,
    Reason Reason,
    string? Note,
    TransactionSnapshotResponse Transaction,
    Money? ExpectedAmount,
    Money DisputedAmount,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TimelineEntryResponse> Timeline);
