using System.Text.Json.Serialization;
using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Contracts.Events;

/// <summary>
/// The data of <c>disputedesk.dispute.status-changed.v1</c> (AD-19): exactly these fields, with no customer ID,
/// account, amount, note or actor identity. <c>outcome</c> is written only on a resolve. <c>occurredAt</c> is a UTC
/// <see cref="DateTime"/>, written with <c>Z</c>, like the event's <c>time</c>.
/// </summary>
public sealed record DisputeStatusChangedV1(
    Guid DisputeId,
    DisputeStatus FromStatus,
    DisputeStatus ToStatus,
    DisputeAction Action,
    ActorType ActorType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Outcome? Outcome,
    DateTime OccurredAt,
    int DisputeVersion);
