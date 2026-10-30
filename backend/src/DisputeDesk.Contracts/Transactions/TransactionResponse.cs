using DisputeDesk.Contracts.Disputes;

namespace DisputeDesk.Contracts.Transactions;

/// <summary>
/// One row of <c>GET /api/v1/transactions</c>. The account shows only its last 4 digits and the card only its first 6
/// and last 4 (<c>maskedCard</c> is null when there is no card). <c>notDisputableReason</c> is null
/// exactly when <c>disputable</c> is true. <c>disputeId</c> and <c>customerStatus</c> name the transaction's dispute,
/// in any status, and are both null when it has none.
/// </summary>
public sealed record TransactionResponse(
    string TransactionId,
    string AccountRef,
    string AccountLast4,
    DateTimeOffset TransactionDate,
    string Description,
    Money Amount,
    TransactionDirection Direction,
    TransactionKind Kind,
    TransactionState State,
    string? MaskedCard,
    bool Disputable,
    NotDisputableReason? NotDisputableReason,
    Guid? DisputeId,
    CustomerStatus? CustomerStatus)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the account
    /// reference, the transaction ID and the masked card (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(TransactionResponse);
}
