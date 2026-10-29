using System.Diagnostics;
using System.Text.Json;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Events;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// The publish mapping of the commit (AD-14): the one place a stored event becomes an integration event, or nothing.
/// Stored events are private domain events and never leave as they are; only the fields AD-19 and AD-19a list do
/// (Microsoft Learn, "Domain events versus integration events").
/// </summary>
internal static class CommitRows
{
    /// <summary>
    /// The outbox row of <paramref name="storedEvent"/> under its stored event ID <paramref name="id"/>, or null for a
    /// type that is never published. A type this switch does not name throws, which stops the commit.
    /// </summary>
    public static OutboxMessage? OutboxMessage(Guid id, StoredEvent storedEvent, Activity? activity) => storedEvent switch
    {
        DisputeRaised raised => Message(
            id,
            raised,
            DisputeEvents.RaisedV1,
            new DisputeRaisedV1(raised.DisputeId, DisputeWire.ToWire(raised.Reason), DisputeWire.ToWire(raised.ToStatus), Utc(raised), raised.Version),
            activity),
        ReviewStarted started => StatusChanged(id, started, started.FromStatus, started.ToStatus, Contracts.Disputes.DisputeAction.StartReview, outcome: null, activity),
        DisputeEscalated escalated => StatusChanged(id, escalated, escalated.FromStatus, escalated.ToStatus, Contracts.Disputes.DisputeAction.Escalate, outcome: null, activity),
        DisputeResolved resolved => StatusChanged(id, resolved, resolved.FromStatus, resolved.ToStatus, Contracts.Disputes.DisputeAction.Resolve, DisputeWire.ToWire(resolved.Outcome), activity),
        DisputeWithdrawn withdrawn => StatusChanged(id, withdrawn, withdrawn.FromStatus, withdrawn.ToStatus, Contracts.Disputes.DisputeAction.Withdraw, outcome: null, activity),
        // For operations only: never published (AD-18).
        SlaWarningRaised => null,
        _ => throw new ArgumentOutOfRangeException(nameof(storedEvent), storedEvent.GetType().Name, "No publish mapping for this stored event type."),
    };

    private static OutboxMessage StatusChanged(
        Guid id, StoredEvent storedEvent, DisputeStatus from, DisputeStatus to, Contracts.Disputes.DisputeAction action, Contracts.Disputes.Outcome? outcome, Activity? activity) =>
        Message(
            id,
            storedEvent,
            DisputeEvents.StatusChangedV1,
            new DisputeStatusChangedV1(
                storedEvent.DisputeId,
                DisputeWire.ToWire(from),
                DisputeWire.ToWire(to),
                action,
                DisputeWire.ToWire(storedEvent.ActorType),
                outcome,
                Utc(storedEvent),
                storedEvent.Version),
            activity);

    /// <summary>
    /// The structured CloudEvent, serialised once into <see cref="Persistence.OutboxMessage.Payload"/> (AD-19), stamped with
    /// the stored event's instant (AD-4). The trace context is the W3C <c>traceparent</c> of the current activity, which
    /// <see cref="Activity.Id"/> is in the default W3C format.
    /// </summary>
    private static OutboxMessage Message<TData>(Guid id, StoredEvent storedEvent, string type, TData data, Activity? activity)
    {
        var cloudEvent = new CloudEvent<TData>(
            DisputeEvents.SpecVersion,
            id.ToString(),
            DisputeEvents.Source,
            type,
            storedEvent.DisputeId.ToString(),
            Utc(storedEvent),
            DisputeEvents.DataContentType,
            DisputeEvents.SchemaFor(type),
            data);

        return new OutboxMessage
        {
            Id = id,
            EventType = type,
            DisputeId = storedEvent.DisputeId,
            DisputeVersion = storedEvent.Version,
            Payload = JsonSerializer.Serialize(cloudEvent, DisputeDeskJson.Options),
            TraceParent = activity?.Id,
            TraceState = activity?.TraceStateString,
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = storedEvent.OccurredAtUtc,
            CreatedAtUtc = storedEvent.OccurredAtUtc,
        };
    }

    // A UTC DateTime, which System.Text.Json writes with Z (the event records say why).
    private static DateTime Utc(StoredEvent storedEvent) => storedEvent.OccurredAtUtc.UtcDateTime;
}
