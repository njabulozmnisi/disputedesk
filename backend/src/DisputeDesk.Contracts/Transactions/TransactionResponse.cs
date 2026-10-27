namespace DisputeDesk.Contracts.Transactions;

/// <summary>
/// One row of <c>GET /api/v1/transactions</c>. The account shows only its last 4 digits and the card only its first 6
/// and last 4 (<c>maskedCard</c> is null when there is no card). <c>notDisputableReason</c> is null
/// exactly when <c>disputable</c> is true.
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
    NotDisputableReason? NotDisputableReason);
