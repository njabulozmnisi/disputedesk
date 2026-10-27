namespace DisputeDesk.Domain.Transactions;

/// <summary>Whether money left the account or arrived in it.</summary>
public enum TransactionDirection
{
    Debit,
    Credit,
}
