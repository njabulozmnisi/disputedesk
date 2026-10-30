using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// A transaction as core banking reports it. Account and card numbers are masked: the account by its last 4
/// digits, the card by its first 6 and last 4 (<see cref="MaskedCard"/> is null when there is no card).
/// </summary>
public sealed record Transaction(
    string TransactionId,
    string AccountRef,
    string AccountLast4,
    DateTimeOffset TransactionDate,
    string Description,
    decimal Amount,
    string Currency,
    TransactionDirection Direction,
    TransactionKind Kind,
    TransactionState State,
    string? MaskedCard)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the account
    /// reference, the transaction ID and the masked card (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(Transaction);
}
