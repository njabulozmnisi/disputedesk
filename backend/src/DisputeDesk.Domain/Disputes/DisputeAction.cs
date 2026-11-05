namespace DisputeDesk.Domain.Disputes;

// Glossary: Action. Raise creates the dispute. The summary below is the API document's text (openapi.json).
/// <summary>A named change to a dispute. On the wire: <c>raise</c>, <c>startReview</c>, and so on.</summary>
public enum DisputeAction
{
    Raise,
    StartReview,
    Escalate,
    Resolve,
    Withdraw,
}
