namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// The body of <c>GET /api/v1/disputes</c> (AD-5): one page, newest first. Pass <c>nextCursor</c> back as
/// <c>cursor</c> for the next page; it is null on the last page.
/// </summary>
public sealed record DisputePageResponse(IReadOnlyList<DisputeSummaryResponse> Items, string? NextCursor)
{
    /// <summary>Only the type name, like the disputes it holds (OWASP Logging Cheat Sheet: data to exclude).</summary>
    public sealed override string ToString() => nameof(DisputePageResponse);
}
