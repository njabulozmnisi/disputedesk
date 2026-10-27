namespace DisputeDesk.Contracts.Transactions;

/// <summary>What kind of movement a transaction is.</summary>
public enum TransactionKind
{
    Card,
    OwnAccountTransfer,
    Transfer,
    Other,
}
