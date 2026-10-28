using System.Globalization;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Transactions;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// A dispute as its customer sees it (AD-13), mapped once here: the customer status, "you" or "the bank" for the
/// actor, and no actor identity, SLA or due date.
/// </summary>
public static class CustomerView
{
    public static DisputeResponse ToResponse(Dispute dispute) => new(
        dispute.Id,
        DisputeWire.ToCustomerStatus(dispute.Status),
        DisputeWire.ToWire(dispute.Reason),
        dispute.Note,
        new TransactionSnapshotResponse(
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
        [.. dispute.Timeline.Select(ToResponse)]);

    private static TimelineEntryResponse ToResponse(TimelineEntry entry) => new(
        DisputeWire.ToWire(entry.Action),
        DisputeWire.ToCustomerStatus(entry.ToStatus),
        DisputeWire.ToTimelineActor(entry.ActorType),
        entry.OccurredAtUtc,
        entry.VisibleNote);

    // AD-4: exactly two decimals, as a string.
    private static Money ToMoney(decimal amount, string currency) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), currency);
}
