namespace DisputeDesk.Contracts.Problems;

/// <summary>Problem slugs of the dispute endpoints (AD-3).</summary>
public static class DisputeProblems
{
    public const string AlreadyDisputed = "already-disputed";

    /// <summary>Carries the <c>reason</c> extension.</summary>
    public const string NotDisputable = "not-disputable";

    public const string IdempotencyInProgress = "idempotency-in-progress";
    public const string IdempotencyMismatch = "idempotency-mismatch";
}
