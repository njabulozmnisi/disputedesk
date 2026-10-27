namespace DisputeDesk.Application.Abstractions;

/// <summary>One page of transactions. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record TransactionPage(IReadOnlyList<Transaction> Items, string? NextCursor);
