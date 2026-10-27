namespace DisputeDesk.CoreBankingStub;

/// <summary>A transaction as the stub returns it: the date resolved to a UTC instant, the account shown by its last 4 digits.</summary>
public sealed record TransactionRow(
    string TransactionId,
    string AccountRef,
    string AccountLast4,
    DateTimeOffset TransactionDate,
    string Description,
    decimal Amount,
    string Currency,
    string Direction,
    string Kind,
    string State,
    string? MaskedCard);
