namespace DisputeDesk.Domain.Disputes;

/// <summary>A named change to a dispute (glossary: Action). <see cref="Raise"/> creates it.</summary>
public enum DisputeAction
{
    Raise,
    StartReview,
    Escalate,
    Resolve,
    Withdraw,
}
