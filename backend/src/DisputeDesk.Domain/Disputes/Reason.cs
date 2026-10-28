namespace DisputeDesk.Domain.Disputes;

/// <summary>Why the customer disputes a transaction (glossary: Reason).</summary>
public enum Reason
{
    Unrecognised,
    DuplicateCharge,

    /// <summary>The only reason that takes an expected amount; the dispute is then for the difference.</summary>
    WrongAmount,

    NotReceived,
    CancelledSubscription,
    RefundNotProcessed,
}
