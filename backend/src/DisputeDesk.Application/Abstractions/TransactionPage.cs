namespace DisputeDesk.Application.Abstractions;

/// <summary>One page of transactions. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record TransactionPage(IReadOnlyList<Transaction> Items, string? NextCursor)
{
    /// <summary>Only the type name, like the transactions it holds (OWASP Logging Cheat Sheet: data to exclude).</summary>
    public sealed override string ToString() => nameof(TransactionPage);
}
