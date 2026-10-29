namespace DisputeDesk.Domain.Disputes;

/// <summary>The result of a resolved dispute (glossary: Outcome): Upheld when the customer was right, or Rejected.</summary>
public enum Outcome
{
    Upheld,
    Rejected,
}
