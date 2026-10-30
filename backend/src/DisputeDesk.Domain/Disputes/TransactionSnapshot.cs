using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// The transaction as core banking reported it when the dispute was raised. The dispute stores the date, description,
/// amount, currency, masked card and account's last 4 digits; the direction, kind and state only decide disputability.
/// </summary>
public sealed record TransactionSnapshot(
    string AccountRef,
    string TransactionId,
    string AccountLast4,
    DateTimeOffset TransactionDate,
    string Description,
    decimal Amount,
    string Currency,
    string? MaskedCard,
    TransactionDirection Direction,
    TransactionKind Kind,
    TransactionState State)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the account
    /// reference, the transaction ID and the masked card (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(TransactionSnapshot);
}
