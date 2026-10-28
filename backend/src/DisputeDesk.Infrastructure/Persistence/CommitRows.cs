using System.Diagnostics;
using System.Text.Json;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Events;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// The audit entry and the outbox row the commit (AD-14) writes for each dispute change, both stamped with the
/// change's instant (AD-4) and the current trace (AD-20).
/// </summary>
internal static class CommitRows
{
    // The API's JSON settings (AD-5), so events and responses spell names and enum values the same way.
    private static readonly JsonSerializerOptions EventJson = CreateEventJson();

    public static AuditLogEntry AuditEntry(Dispute dispute, DisputeChange change, Activity? activity) => new()
    {
        Id = Guid.CreateVersion7(change.OccurredAt),
        OccurredAtUtc = change.OccurredAt,
        Action = ToAuditAction(change.Action),
        Result = AuditResult.Succeeded,
        ActorType = change.Actor.Type,
        ActorId = change.Actor.Id,
        CustomerId = dispute.CustomerId,
        DisputeId = dispute.Id,
        TraceId = activity?.TraceId.ToHexString(),
    };

    /// <summary>
    /// The structured CloudEvent, serialised once into <see cref="OutboxMessage.Payload"/> (AD-19). The trace context is
    /// the W3C <c>traceparent</c> of the current activity, which <see cref="Activity.Id"/> is in the default W3C format.
    /// </summary>
    public static OutboxMessage OutboxMessage(Dispute dispute, DisputeChange change, Activity? activity)
    {
        if (change.Action != DisputeAction.Raise)
        {
            throw new NotSupportedException($"No dispute event is defined yet for the action {change.Action}.");
        }

        var eventId = Guid.CreateVersion7(change.OccurredAt);
        // A UTC DateTime, which System.Text.Json writes with Z (the event records say why).
        DateTime occurredAt = change.OccurredAt.UtcDateTime;
        var raised = new CloudEvent<DisputeRaisedV1>(
            DisputeEvents.SpecVersion,
            eventId.ToString(),
            DisputeEvents.Source,
            DisputeEvents.RaisedV1,
            dispute.Id.ToString(),
            occurredAt,
            DisputeEvents.DataContentType,
            DisputeEvents.SchemaFor(DisputeEvents.RaisedV1),
            new DisputeRaisedV1(dispute.Id, DisputeWire.ToWire(dispute.Reason), DisputeWire.ToWire(change.ToStatus), occurredAt, change.Version));

        return new OutboxMessage
        {
            Id = eventId,
            EventType = DisputeEvents.RaisedV1,
            DisputeId = dispute.Id,
            DisputeVersion = change.Version,
            Payload = JsonSerializer.Serialize(raised, EventJson),
            TraceParent = activity?.Id,
            TraceState = activity?.TraceStateString,
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = change.OccurredAt,
            CreatedAtUtc = change.OccurredAt,
        };
    }

    private static AuditAction ToAuditAction(DisputeAction action) => action switch
    {
        DisputeAction.Raise => AuditAction.Raise,
        DisputeAction.StartReview => AuditAction.StartReview,
        DisputeAction.Escalate => AuditAction.Escalate,
        DisputeAction.Resolve => AuditAction.Resolve,
        DisputeAction.Withdraw => AuditAction.Withdraw,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unmapped action."),
    };

    private static JsonSerializerOptions CreateEventJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DisputeDeskJson.Apply(options);
        return options;
    }
}
