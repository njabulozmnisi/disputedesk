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

    /// <summary>
    /// The login or the client IP reached its linking rate limit (FR-8). One fixed answer for both limits, which names
    /// neither the partition nor a count; the rate limiter adds <c>Retry-After</c>.
    /// </summary>
    public static readonly Error RateLimited = Error.RateLimited("rate-limited", "Too many linking requests. Try again later.");

    /// <summary>
    /// The customer or the login already has a link that another verification committed first (AD-15). The verify handler
    /// answers it as a wrong code, so it never reaches HTTP.
    /// </summary>
    public static readonly Error LinkClash = Error.Conflict("action-not-allowed", "Another verification linked this customer or login first.");

    /// <summary>Three codes were wrong: this challenge takes no more attempts, and the person starts linking again (FR-7).</summary>
    public static readonly Error Locked = Error.Conflict("linking-locked", "Too many incorrect codes. Start linking again to get a new code.");

    /// <summary>
    /// A wrong, expired or unknown code, with the attempts left. Every kind of challenge, and a login with none, gets this
    /// same answer (OWASP Authentication Cheat Sheet, generic responses).
    /// </summary>
    public static Error OtpIncorrect(int attemptsRemaining) => Error.BusinessRule(
        "otp-incorrect",
        "The code is incorrect or has expired.",
        new Dictionary<string, object?> { ["attemptsRemaining"] = attemptsRemaining });
}
