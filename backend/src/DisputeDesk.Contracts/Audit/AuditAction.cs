namespace DisputeDesk.Contracts.Audit;

/// <summary>What an audit entry records (AD-14a): a closed set, stored as the member name.</summary>
public enum AuditAction
{
    Raise,
    StartReview,
    Escalate,
    Resolve,
    Withdraw,
    SignIn,
    SignOut,
    LinkingStart,
    LinkingNoMatch,
    LinkingAlreadyLinked,
    LinkingSimSwapFailed,
    LinkingSendCapped,
    LinkingOtpSent,
    LinkingOtpIncorrect,
    LinkingOtpExpired,
    LinkingLocked,
    LinkingRateLimited,
    Linked,
}
