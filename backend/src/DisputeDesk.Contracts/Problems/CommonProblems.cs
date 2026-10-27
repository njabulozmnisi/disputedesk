namespace DisputeDesk.Contracts.Problems;

/// <summary>Problem slugs any endpoint can return (AD-3).</summary>
public static class CommonProblems
{
    public const string Validation = "validation";
    public const string NotFound = "not-found";

    /// <summary>Carries the caller's view of the current status (<c>customerStatus</c> or <c>status</c>).</summary>
    public const string ActionNotAllowed = "action-not-allowed";

    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string RateLimited = "rate-limited";
    public const string Internal = "internal";
}
