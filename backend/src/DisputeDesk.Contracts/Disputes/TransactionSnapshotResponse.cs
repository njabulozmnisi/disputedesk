using DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Contracts.Disputes;

/// <summary>The disputed transaction as core banking reported it when the dispute was raised.</summary>
public sealed record TransactionSnapshotResponse(
    string TransactionId,
    string AccountRef,
    string AccountLast4,
    DateTimeOffset TransactionDate,
    string Description,
    Money Amount,
    string? MaskedCard)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the account
    /// reference, the transaction ID and the masked card (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(TransactionSnapshotResponse);
}
