namespace DisputeDesk.Domain.Disputes;

// Glossary: Status. Customers see Escalated as under review (prioritised). The summary below is the API document's text (openapi.json).
/// <summary>A dispute's internal status, as events and operations see it. Customers see CustomerStatus instead.</summary>
public enum DisputeStatus
{
    Submitted,
    UnderReview,
    Escalated,
    Resolved,
    Withdrawn,
}
