namespace DisputeDesk.Contracts.Transactions;

/// <summary>
/// The body of <c>GET /api/v1/transactions</c> (AD-5): one page, newest first. Pass <c>nextCursor</c> back as
/// <c>cursor</c> for the next page; it is null on the last page.
/// </summary>
public sealed record TransactionPageResponse(IReadOnlyList<TransactionResponse> Items, string? NextCursor)
{
    /// <summary>Only the type name, like the transactions it holds (OWASP Logging Cheat Sheet: data to exclude).</summary>
    public sealed override string ToString() => nameof(TransactionPageResponse);
}
