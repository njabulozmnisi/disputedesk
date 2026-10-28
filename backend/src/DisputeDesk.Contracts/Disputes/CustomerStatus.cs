namespace DisputeDesk.Contracts.Disputes;

/// <summary>A dispute's status as the customer sees it (AD-13): escalated shows as <see cref="UnderReviewPrioritised"/>.</summary>
public enum CustomerStatus
{
    Submitted,
    UnderReview,
    UnderReviewPrioritised,
    Resolved,
    Withdrawn,
}
