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
    string? MaskedCard);
