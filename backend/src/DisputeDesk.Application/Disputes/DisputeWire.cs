using System.Globalization;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Transactions;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// What the customer sees in place of the Domain enums (AD-13): the customer status and the timeline actor. The enums
/// identical on the wire are the Domain types themselves (AD-1), so they need no mapping. The errors that carry a wire
/// status and the wire's money format are built here too.
/// </summary>
public static class DisputeWire
{
    /// <summary>The status as the customer sees it (AD-13): escalated is under review (prioritised).</summary>
    public static CustomerStatus ToCustomerStatus(DisputeStatus status) => status switch
    {
        DisputeStatus.Submitted => CustomerStatus.Submitted,
        DisputeStatus.UnderReview => CustomerStatus.UnderReview,
        DisputeStatus.Escalated => CustomerStatus.UnderReviewPrioritised,
        DisputeStatus.Resolved => CustomerStatus.Resolved,
        DisputeStatus.Withdrawn => CustomerStatus.Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
    };

    /// <summary>Who acted, as the customer sees it (AD-13): the customer is "you"; operations and automation are "the bank".</summary>
    public static TimelineActor ToTimelineActor(ActorType actorType) =>
        actorType == ActorType.Customer ? TimelineActor.You : TimelineActor.TheBank;

    /// <summary>An amount on the wire: two decimals, invariant culture, with its ISO currency code.</summary>
    public static Money ToMoney(decimal amount, string currency) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), currency);

    /// <summary>
    /// <see cref="DisputeErrors.AlreadyDisputed"/> for a customer: the problem names their dispute and its
    /// status as they see it (AD-13).
    /// </summary>
    public static Error AlreadyDisputedAs(Guid disputeId, CustomerStatus customerStatus) => Error.Conflict(
        "already-disputed",
        DisputeErrors.AlreadyDisputed.Message,
        new Dictionary<string, object?> { ["disputeId"] = disputeId, ["customerStatus"] = customerStatus });

    /// <summary>
    /// <see cref="DisputeErrors.ActionNotAllowed"/> for a customer: the problem carries the status as they
    /// see it (AD-13).
    /// </summary>
    public static Error ActionNotAllowedAs(CustomerStatus customerStatus) => Error.ActionNotAllowed(
        "action-not-allowed",
        DisputeErrors.ActionNotAllowed.Message,
        new Dictionary<string, object?> { ["customerStatus"] = customerStatus });

    /// <summary>
    /// <see cref="DisputeErrors.ActionNotAllowed"/> for operations: the problem carries the internal status
    /// under <c>disputeStatus</c>, because RFC 9457 §3.1 keeps <c>status</c> for the HTTP status code.
    /// </summary>
    public static Error ActionNotAllowedWithDisputeStatus(DisputeStatus disputeStatus) => Error.ActionNotAllowed(
        "action-not-allowed",
        DisputeErrors.ActionNotAllowed.Message,
        new Dictionary<string, object?> { ["disputeStatus"] = disputeStatus });
}
