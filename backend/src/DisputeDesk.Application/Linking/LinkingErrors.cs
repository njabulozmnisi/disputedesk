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

    /// <summary>
    /// The linking queue is full (AD-12). The same for every start, whatever its details, and with no
    /// <c>Retry-After</c>: a full queue has no window to report.
    /// </summary>
    public static readonly Error QueueFull = Error.RateLimited("rate-limited", "Too many linking starts are waiting. Try again later.");
}
