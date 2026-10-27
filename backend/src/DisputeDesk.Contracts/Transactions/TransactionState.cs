namespace DisputeDesk.Contracts.Transactions;

/// <summary>Whether a transaction has settled or is still pending.</summary>
public enum TransactionState
{
    Settled,
    Pending,
}
