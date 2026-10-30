using System.Globalization;
using DisputeDesk.Contracts.Transactions;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// A dispute as its customer sees it (AD-13), mapped once here: the customer status, "you" or "the bank" for the
/// actor, and no actor identity, SLA or due date. The timeline is built from the dispute's stored events. The raise 201
/// and the detail use <see cref="ToResponse"/>, the history list <see cref="ToSummary"/>.
/// </summary>
public static class CustomerView
{
    public static Contracts.Disputes.DisputeResponse ToResponse(Dispute dispute, IEnumerable<StoredEvent> storedEvents) => new(
        dispute.Id,
        DisputeWire.ToCustomerStatus(dispute.Status),
        dispute.Outcome is Outcome outcome ? DisputeWire.ToWire(outcome) : null,
        DisputeWire.ToWire(dispute.Reason),
        dispute.Note,
        ToSnapshot(dispute),
        dispute.ExpectedAmount is decimal expected ? ToMoney(expected, dispute.Currency) : null,
        ToMoney(dispute.DisputedAmount, dispute.Currency),
        dispute.CreatedAtUtc,
        ToTimeline(storedEvents));

    /// <summary>One row of the history list: the same fields as <see cref="ToResponse"/>, without the note, expected amount or timeline.</summary>
    public static Contracts.Disputes.DisputeSummaryResponse ToSummary(Dispute dispute) => new(
        dispute.Id,
        DisputeWire.ToCustomerStatus(dispute.Status),
        dispute.Outcome is Outcome outcome ? DisputeWire.ToWire(outcome) : null,
        DisputeWire.ToWire(dispute.Reason),
        ToSnapshot(dispute),
        ToMoney(dispute.DisputedAmount, dispute.Currency),
        dispute.CreatedAtUtc);

    /// <summary>The customer's timeline: one entry for each stored event the allowlist admits, in stream order.</summary>
    public static IReadOnlyList<Contracts.Disputes.TimelineEntryResponse> ToTimeline(IEnumerable<StoredEvent> storedEvents) =>
        [.. storedEvents.Select(ToTimelineEntry).OfType<Contracts.Disputes.TimelineEntryResponse>()];

    /// <summary>
    /// The allowlist (AD-13; OWASP Authorization Cheat Sheet, deny by default): a type test admits exactly the five
    /// lifecycle types. Every other type, <see cref="SlaWarningRaised"/> and any type added later, gives no entry.
    /// </summary>
    private static Contracts.Disputes.TimelineEntryResponse? ToTimelineEntry(StoredEvent storedEvent) => storedEvent switch
    {
        DisputeRaised raised => Entry(raised, DisputeAction.Raise, raised.ToStatus, raised.Note),
        ReviewStarted started => Entry(started, DisputeAction.StartReview, started.ToStatus, note: null),
        DisputeEscalated escalated => Entry(escalated, DisputeAction.Escalate, escalated.ToStatus, note: null),
        DisputeResolved resolved => Entry(resolved, DisputeAction.Resolve, resolved.ToStatus, resolved.Note),
        DisputeWithdrawn withdrawn => Entry(withdrawn, DisputeAction.Withdraw, withdrawn.ToStatus, withdrawn.Note),
        _ => null,
    };

    private static Contracts.Disputes.TimelineEntryResponse Entry(StoredEvent storedEvent, DisputeAction action, DisputeStatus toStatus, string? note) => new(
        DisputeWire.ToWire(action),
        DisputeWire.ToCustomerStatus(toStatus),
        DisputeWire.ToTimelineActor(storedEvent.ActorType),
        storedEvent.OccurredAtUtc,
        note);

    private static Contracts.Disputes.TransactionSnapshotResponse ToSnapshot(Dispute dispute) => new(
        dispute.TransactionId,
        dispute.AccountRef,
        dispute.AccountLast4,
        dispute.TransactionDateUtc,
        dispute.Description,
        ToMoney(dispute.TransactionAmount, dispute.Currency),
        dispute.MaskedCard);

    // AD-4: exactly two decimals, as a string.
    private static Money ToMoney(decimal amount, string currency) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), currency);
}
