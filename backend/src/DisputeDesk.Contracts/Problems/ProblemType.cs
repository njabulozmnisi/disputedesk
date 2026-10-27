namespace DisputeDesk.Contracts.Problems;

/// <summary>
/// The ProblemDetails <c>type</c> value for a slug (AD-3). Tag URIs, because the project owns no domain; the SPA
/// routes on the slug after the prefix.
/// </summary>
public static class ProblemType
{
    public const string Prefix = "tag:disputedesk,2026:problem:";

    public static string For(string slug) => Prefix + slug;
}
