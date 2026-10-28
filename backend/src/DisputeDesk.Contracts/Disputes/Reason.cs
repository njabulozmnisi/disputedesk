namespace DisputeDesk.Contracts.Disputes;

/// <summary>Why the customer disputes a transaction. On the wire: <c>unrecognised</c>, <c>duplicateCharge</c>, and so on.</summary>
public enum Reason
{
    Unrecognised,
    DuplicateCharge,

    /// <summary>The only reason that takes <c>expectedAmount</c>.</summary>
    WrongAmount,

    NotReceived,
    CancelledSubscription,
    RefundNotProcessed,
}
