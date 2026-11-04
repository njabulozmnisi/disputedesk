using System.Globalization;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// The dispute enums between Domain and the wire. Contracts reference no other project (AD-1), so the wire enums are
/// their own types; these switches are the one place they meet, for the API and the events alike. The errors that carry
/// a wire status and the wire's money format are built here too.
/// </summary>
public static class DisputeWire
{
    public static Domain.Disputes.Reason ToDomain(Reason reason) => reason switch
    {
        Reason.Unrecognised => Domain.Disputes.Reason.Unrecognised,
        Reason.DuplicateCharge => Domain.Disputes.Reason.DuplicateCharge,
        Reason.WrongAmount => Domain.Disputes.Reason.WrongAmount,
        Reason.NotReceived => Domain.Disputes.Reason.NotReceived,
        Reason.CancelledSubscription => Domain.Disputes.Reason.CancelledSubscription,
        Reason.RefundNotProcessed => Domain.Disputes.Reason.RefundNotProcessed,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped reason."),
    };

    public static Domain.Disputes.Outcome ToDomain(Outcome outcome) => outcome switch
    {
        Outcome.Upheld => Domain.Disputes.Outcome.Upheld,
        Outcome.Rejected => Domain.Disputes.Outcome.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unmapped outcome."),
    };

    public static Reason ToWire(Domain.Disputes.Reason reason) => reason switch
    {
        Domain.Disputes.Reason.Unrecognised => Reason.Unrecognised,
        Domain.Disputes.Reason.DuplicateCharge => Reason.DuplicateCharge,
        Domain.Disputes.Reason.WrongAmount => Reason.WrongAmount,
        Domain.Disputes.Reason.NotReceived => Reason.NotReceived,
        Domain.Disputes.Reason.CancelledSubscription => Reason.CancelledSubscription,
        Domain.Disputes.Reason.RefundNotProcessed => Reason.RefundNotProcessed,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped reason."),
    };

    /// <summary>The internal status, for events and operations.</summary>
    public static DisputeStatus ToWire(Domain.Disputes.DisputeStatus status) => status switch
    {
        Domain.Disputes.DisputeStatus.Submitted => DisputeStatus.Submitted,
        Domain.Disputes.DisputeStatus.UnderReview => DisputeStatus.UnderReview,
        Domain.Disputes.DisputeStatus.Escalated => DisputeStatus.Escalated,
        Domain.Disputes.DisputeStatus.Resolved => DisputeStatus.Resolved,
        Domain.Disputes.DisputeStatus.Withdrawn => DisputeStatus.Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
    };

    public static DisputeAction ToWire(Domain.Disputes.DisputeAction action) => action switch
    {
        Domain.Disputes.DisputeAction.Raise => DisputeAction.Raise,
        Domain.Disputes.DisputeAction.StartReview => DisputeAction.StartReview,
        Domain.Disputes.DisputeAction.Escalate => DisputeAction.Escalate,
        Domain.Disputes.DisputeAction.Resolve => DisputeAction.Resolve,
        Domain.Disputes.DisputeAction.Withdraw => DisputeAction.Withdraw,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unmapped action."),
    };

    /// <summary>Who acted, for events and operations (AD-19).</summary>
    public static ActorType ToWire(Domain.Disputes.ActorType actorType) => actorType switch
    {
        Domain.Disputes.ActorType.Customer => ActorType.Customer,
        Domain.Disputes.ActorType.Operations => ActorType.Operations,
        Domain.Disputes.ActorType.Automation => ActorType.Automation,
        _ => throw new ArgumentOutOfRangeException(nameof(actorType), actorType, "Unmapped actor type."),
    };

    public static Outcome ToWire(Domain.Disputes.Outcome outcome) => outcome switch
    {
        Domain.Disputes.Outcome.Upheld => Outcome.Upheld,
        Domain.Disputes.Outcome.Rejected => Outcome.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unmapped outcome."),
    };

    /// <summary>The status as the customer sees it (AD-13): escalated is under review (prioritised).</summary>
    public static CustomerStatus ToCustomerStatus(Domain.Disputes.DisputeStatus status) => status switch
    {
        Domain.Disputes.DisputeStatus.Submitted => CustomerStatus.Submitted,
        Domain.Disputes.DisputeStatus.UnderReview => CustomerStatus.UnderReview,
        Domain.Disputes.DisputeStatus.Escalated => CustomerStatus.UnderReviewPrioritised,
        Domain.Disputes.DisputeStatus.Resolved => CustomerStatus.Resolved,
        Domain.Disputes.DisputeStatus.Withdrawn => CustomerStatus.Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
    };

    /// <summary>Who acted, as the customer sees it (AD-13): the customer is "you"; operations and automation are "the bank".</summary>
    public static TimelineActor ToTimelineActor(Domain.Disputes.ActorType actorType) =>
        actorType == Domain.Disputes.ActorType.Customer ? TimelineActor.You : TimelineActor.TheBank;

    /// <summary>An amount on the wire: two decimals, invariant culture, with its ISO currency code.</summary>
    public static Money ToMoney(decimal amount, string currency) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), currency);

    /// <summary>
    /// <see cref="Domain.Disputes.DisputeErrors.AlreadyDisputed"/> for a customer: the problem names their dispute and its
    /// status as they see it (AD-13).
    /// </summary>
    public static Domain.Error AlreadyDisputedAs(Guid disputeId, CustomerStatus customerStatus) => Domain.Error.Conflict(
        "already-disputed",
        Domain.Disputes.DisputeErrors.AlreadyDisputed.Message,
        new Dictionary<string, object?> { ["disputeId"] = disputeId, ["customerStatus"] = customerStatus });

    /// <summary>
    /// <see cref="Domain.Disputes.DisputeErrors.ActionNotAllowed"/> for a customer: the problem carries the status as they
    /// see it (AD-13).
    /// </summary>
    public static Domain.Error ActionNotAllowedAs(CustomerStatus customerStatus) => Domain.Error.ActionNotAllowed(
        "action-not-allowed",
        Domain.Disputes.DisputeErrors.ActionNotAllowed.Message,
        new Dictionary<string, object?> { ["customerStatus"] = customerStatus });

    /// <summary>
    /// <see cref="Domain.Disputes.DisputeErrors.ActionNotAllowed"/> for operations: the problem carries the internal status
    /// under <c>disputeStatus</c>, because RFC 9457 §3.1 keeps <c>status</c> for the HTTP status code.
    /// </summary>
    public static Domain.Error ActionNotAllowedWithDisputeStatus(DisputeStatus disputeStatus) => Domain.Error.ActionNotAllowed(
        "action-not-allowed",
        Domain.Disputes.DisputeErrors.ActionNotAllowed.Message,
        new Dictionary<string, object?> { ["disputeStatus"] = disputeStatus });
}
