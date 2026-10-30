using System.Globalization;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Operations;
using DisputeDesk.Contracts.Transactions;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Operations;

/// <summary>
/// A dispute as operations see it (FR-30; AD-13), mapped field by field here, never by serialising a stored event or its
/// data: the internal status and actor type, every stored event type on the timeline, and nothing personal beyond the
/// transaction snapshot. Notes the customer wrote (the raise and withdraw notes) are left out; the bank's resolve note
/// stays on its entry.
/// </summary>
public static class OperationsView
{
    public static OperationsDisputeResponse ToResponse(Dispute dispute, IEnumerable<StoredEvent> storedEvents) => new(
        dispute.Id,
        DisputeWire.ToWire(dispute.Status),
        dispute.Outcome is Outcome outcome ? DisputeWire.ToWire(outcome) : null,
        DisputeWire.ToWire(dispute.Reason),
        new Contracts.Disputes.TransactionSnapshotResponse(
            dispute.TransactionId,
            dispute.AccountRef,
            dispute.AccountLast4,
            dispute.TransactionDateUtc,
            dispute.Description,
            ToMoney(dispute.TransactionAmount, dispute.Currency),
            dispute.MaskedCard),
        dispute.ExpectedAmount is decimal expected ? ToMoney(expected, dispute.Currency) : null,
        ToMoney(dispute.DisputedAmount, dispute.Currency),
        dispute.CreatedAtUtc,
        [.. storedEvents.Select(ToTimelineEntry)]);

    /// <summary>
    /// One entry for every stored event type. A type added later and not mapped here throws, so it is never shown
    /// unreviewed; a stored row of a type this code does not know is skipped before it becomes a stored event (AD-13).
    /// </summary>
    private static OperationsTimelineEntryResponse ToTimelineEntry(StoredEvent storedEvent) => storedEvent switch
    {
        DisputeRaised raised => Entry(raised, StoredEventType.DisputeRaised, fromStatus: null, raised.ToStatus, note: null),
        ReviewStarted started => Entry(started, StoredEventType.ReviewStarted, started.FromStatus, started.ToStatus, note: null),
        DisputeEscalated escalated => Entry(escalated, StoredEventType.DisputeEscalated, escalated.FromStatus, escalated.ToStatus, note: null),
        DisputeResolved resolved => Entry(resolved, StoredEventType.DisputeResolved, resolved.FromStatus, resolved.ToStatus, resolved.Note),
        DisputeWithdrawn withdrawn => Entry(withdrawn, StoredEventType.DisputeWithdrawn, withdrawn.FromStatus, withdrawn.ToStatus, note: null),
        SlaWarningRaised warning => Entry(warning, StoredEventType.SlaWarningRaised, fromStatus: null, toStatus: null, note: null),
        _ => throw new ArgumentOutOfRangeException(nameof(storedEvent), storedEvent.GetType().Name, "Unmapped stored event type."),
    };

    private static OperationsTimelineEntryResponse Entry(
        StoredEvent storedEvent, StoredEventType eventType, DisputeStatus? fromStatus, DisputeStatus? toStatus, string? note) => new(
        eventType,
        DisputeWire.ToWire(storedEvent.ActorType),
        storedEvent.OccurredAtUtc,
        fromStatus is DisputeStatus from ? DisputeWire.ToWire(from) : null,
        toStatus is DisputeStatus to ? DisputeWire.ToWire(to) : null,
        note);

    // AD-4: exactly two decimals, as a string.
    private static Money ToMoney(decimal amount, string currency) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), currency);
}
