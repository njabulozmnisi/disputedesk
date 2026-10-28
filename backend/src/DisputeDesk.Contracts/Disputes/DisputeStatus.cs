namespace DisputeDesk.Contracts.Disputes;

/// <summary>A dispute's internal status, as events and operations see it. Customers see <see cref="CustomerStatus"/> instead.</summary>
public enum DisputeStatus
{
    Submitted,
    UnderReview,
    Escalated,
    Resolved,
    Withdrawn,
}
