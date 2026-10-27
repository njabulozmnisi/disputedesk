namespace DisputeDesk.Contracts.Problems;

/// <summary>Problem slugs of linking and the authorisation states (AD-3, AD-10).</summary>
public static class LinkingProblems
{
    public const string LinkingRequired = "linking-required";
    public const string AlreadyLinked = "already-linked";

    /// <summary>Carries the <c>attemptsRemaining</c> extension.</summary>
    public const string OtpIncorrect = "otp-incorrect";

    public const string LinkingLocked = "linking-locked";
}
