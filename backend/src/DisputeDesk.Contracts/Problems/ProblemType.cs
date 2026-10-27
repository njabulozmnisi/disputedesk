namespace DisputeDesk.Contracts.Problems;

/// <summary>
/// The ProblemDetails <c>type</c> value for a slug (AD-3): the URL of the slug's page under <c>docs/problems/</c>, a
/// resolvable type URI as RFC 9457 §3.1.1 encourages. The SPA routes on the slug, the last path segment without
/// <c>.md</c>.
/// </summary>
public static class ProblemType
{
    public const string Prefix = "https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/";

    public static string For(string slug) => Prefix + slug + ".md";
}
