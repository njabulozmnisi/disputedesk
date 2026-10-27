namespace DisputeDesk.Domain.Transactions;

/// <summary>What kind of movement a transaction is. Only card transactions can be disputed.</summary>
public enum TransactionKind
{
    Card,
    OwnAccountTransfer,
    Transfer,
    Other,
}
