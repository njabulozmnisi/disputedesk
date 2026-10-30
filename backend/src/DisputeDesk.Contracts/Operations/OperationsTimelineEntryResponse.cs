using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Contracts.Operations;

/// <summary>
/// One stored event as operations see it (FR-30): its type, who acted (the actor type, never the identity), when, and
/// the statuses it moved between. <c>fromStatus</c> is null on <c>disputeRaised</c> and <c>slaWarningRaised</c>, and
/// <c>toStatus</c> on <c>slaWarningRaised</c>. <c>note</c> is set only on <c>disputeResolved</c>: the bank's note
/// meant for the customer.
/// </summary>
public sealed record OperationsTimelineEntryResponse(
    StoredEventType EventType,
    ActorType ActorType,
    DateTimeOffset OccurredAt,
    DisputeStatus? FromStatus,
    DisputeStatus? ToStatus,
    string? Note);
