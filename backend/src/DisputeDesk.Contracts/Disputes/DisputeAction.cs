namespace DisputeDesk.Contracts.Disputes;

/// <summary>A named change to a dispute. On the wire: <c>raise</c>, <c>startReview</c>, and so on.</summary>
public enum DisputeAction
{
    Raise,
    StartReview,
    Escalate,
    Resolve,
    Withdraw,
}
