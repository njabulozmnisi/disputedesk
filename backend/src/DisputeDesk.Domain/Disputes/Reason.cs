namespace DisputeDesk.Domain.Disputes;

// Glossary: Reason. The summary below is the API document's text (openapi.json).
/// <summary>Why the customer disputes a transaction. On the wire: <c>unrecognised</c>, <c>duplicateCharge</c>, and so on.</summary>
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
