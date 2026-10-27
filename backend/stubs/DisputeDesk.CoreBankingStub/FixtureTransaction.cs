namespace DisputeDesk.CoreBankingStub;

/// <summary>
/// A transaction in the fixtures. Its date is relative: <see cref="DaysAgo"/> days before the start of the current
/// South African day, at <see cref="Time"/> South African time. The card is stored masked already.
/// </summary>
public sealed record FixtureTransaction(
    string TransactionId,
    string AccountRef,
    int DaysAgo,
    TimeOnly Time,
    string Description,
    decimal Amount,
    string Currency,
    string Direction,
    string Kind,
    string State,
    string? MaskedCard);
