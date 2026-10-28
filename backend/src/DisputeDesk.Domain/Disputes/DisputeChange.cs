namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// A change an action made, waiting for the commit (AD-14), which turns it into one audit entry and one dispute event
/// in the same transaction. <see cref="FromStatus"/> is null for <see cref="DisputeAction.Raise"/>.
/// </summary>
public sealed record DisputeChange(
    DisputeAction Action,
    DisputeStatus? FromStatus,
    DisputeStatus ToStatus,
    Actor Actor,
    DateTimeOffset OccurredAt,
    int Version);
