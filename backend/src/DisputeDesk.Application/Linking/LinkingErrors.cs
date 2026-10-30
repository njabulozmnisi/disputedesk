using DisputeDesk.Domain;

namespace DisputeDesk.Application.Linking;

/// <summary>The expected failures of linking. The codes are AD-3 problem slugs.</summary>
public static class LinkingErrors
{
    /// <summary>
    /// Another start by the same login committed its challenge first (AD-15). The start handler answers it with the same
    /// 202 as every start, so it never reaches HTTP.
    /// </summary>
    public static readonly Error ChallengeClash = Error.Conflict("action-not-allowed", "Another linking start by this login committed first.");
}
