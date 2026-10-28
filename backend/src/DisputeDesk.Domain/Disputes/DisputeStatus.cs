namespace DisputeDesk.Domain.Disputes;

/// <summary>Where a dispute is (glossary: Status). Customers see <see cref="Escalated"/> as under review (prioritised).</summary>
public enum DisputeStatus
{
    Submitted,
    UnderReview,
    Escalated,
    Resolved,
    Withdrawn,
}
