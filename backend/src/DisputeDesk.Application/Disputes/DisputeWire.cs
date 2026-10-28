using DisputeDesk.Domain.Disputes;
using Wire = DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// The dispute enums between Domain and the wire. Contracts reference no other project (AD-1), so the wire enums are
/// their own types; these switches are the one place they meet, for the API and the events alike.
/// </summary>
public static class DisputeWire
{
    public static Reason ToDomain(Wire.Reason reason) => reason switch
    {
        Wire.Reason.Unrecognised => Reason.Unrecognised,
        Wire.Reason.DuplicateCharge => Reason.DuplicateCharge,
        Wire.Reason.WrongAmount => Reason.WrongAmount,
        Wire.Reason.NotReceived => Reason.NotReceived,
        Wire.Reason.CancelledSubscription => Reason.CancelledSubscription,
        Wire.Reason.RefundNotProcessed => Reason.RefundNotProcessed,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped reason."),
    };

    public static Wire.Reason ToWire(Reason reason) => reason switch
    {
        Reason.Unrecognised => Wire.Reason.Unrecognised,
        Reason.DuplicateCharge => Wire.Reason.DuplicateCharge,
        Reason.WrongAmount => Wire.Reason.WrongAmount,
        Reason.NotReceived => Wire.Reason.NotReceived,
        Reason.CancelledSubscription => Wire.Reason.CancelledSubscription,
        Reason.RefundNotProcessed => Wire.Reason.RefundNotProcessed,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped reason."),
    };

    /// <summary>The internal status, for events and operations.</summary>
    public static Wire.DisputeStatus ToWire(DisputeStatus status) => status switch
    {
        DisputeStatus.Submitted => Wire.DisputeStatus.Submitted,
        DisputeStatus.UnderReview => Wire.DisputeStatus.UnderReview,
        DisputeStatus.Escalated => Wire.DisputeStatus.Escalated,
        DisputeStatus.Resolved => Wire.DisputeStatus.Resolved,
        DisputeStatus.Withdrawn => Wire.DisputeStatus.Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
    };

    public static Wire.DisputeAction ToWire(DisputeAction action) => action switch
    {
        DisputeAction.Raise => Wire.DisputeAction.Raise,
        DisputeAction.StartReview => Wire.DisputeAction.StartReview,
        DisputeAction.Escalate => Wire.DisputeAction.Escalate,
        DisputeAction.Resolve => Wire.DisputeAction.Resolve,
        DisputeAction.Withdraw => Wire.DisputeAction.Withdraw,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unmapped action."),
    };

    /// <summary>The status as the customer sees it (AD-13): escalated is under review (prioritised).</summary>
    public static Wire.CustomerStatus ToCustomerStatus(DisputeStatus status) => status switch
    {
        DisputeStatus.Submitted => Wire.CustomerStatus.Submitted,
        DisputeStatus.UnderReview => Wire.CustomerStatus.UnderReview,
        DisputeStatus.Escalated => Wire.CustomerStatus.UnderReviewPrioritised,
        DisputeStatus.Resolved => Wire.CustomerStatus.Resolved,
        DisputeStatus.Withdrawn => Wire.CustomerStatus.Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
    };

    /// <summary>Who acted, as the customer sees it (AD-13): the customer is "you"; operations and automation are "the bank".</summary>
    public static Wire.TimelineActor ToTimelineActor(ActorType actorType) =>
        actorType == ActorType.Customer ? Wire.TimelineActor.You : Wire.TimelineActor.TheBank;
}
