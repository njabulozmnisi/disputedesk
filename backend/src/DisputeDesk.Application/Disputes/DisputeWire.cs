using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// The dispute enums between Domain and the wire. Contracts reference no other project (AD-1), so the wire enums are
/// their own types; these switches are the one place they meet, for the API and the events alike.
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
}
