namespace DisputeDesk.Domain.Transactions;

// Only card transactions can be disputed. The summary below is the API document's text (openapi.json).
/// <summary>What kind of movement a transaction is.</summary>
public enum TransactionKind
{
    Card,
    OwnAccountTransfer,
    Transfer,
    Other,
}
