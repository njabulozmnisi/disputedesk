using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Contracts.Events;

/// <summary>
/// The data of <c>disputedesk.dispute.raised.v1</c> (AD-19a): exactly these fields, with no customer ID, amount or
/// note. Consumers read the detail through the operations API. <c>occurredAt</c> is a UTC <see cref="DateTime"/>, written
/// with <c>Z</c>, like the event's <c>time</c>.
/// </summary>
public sealed record DisputeRaisedV1(
    Guid DisputeId,
    Reason Reason,
    DisputeStatus Status,
    DateTime OccurredAt,
    int DisputeVersion);
