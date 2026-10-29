namespace DisputeDesk.Contracts.Disputes;

/// <summary>The result of a resolved dispute (glossary: Outcome). On the wire: <c>upheld</c> or <c>rejected</c>.</summary>
public enum Outcome
{
    Upheld,
    Rejected,
}
