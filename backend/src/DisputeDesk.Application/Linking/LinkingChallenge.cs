namespace DisputeDesk.Application.Linking;

/// <summary>
/// One linking attempt of a login (AD-12, AD-22). Every well-formed start inserts a blank one and supersedes the login's
/// earlier one; the background job fills it only when the details match a customer with no linked login and the
/// SIM-swap check passes. Only its HMAC is stored, never the OTP. At most one per login is current (neither superseded
/// nor consumed), which the unique index <c>UQ_LinkingChallenges_LoginSub</c> enforces (AD-15).
/// </summary>
public sealed class LinkingChallenge
{
    /// <summary>How long an OTP can be used, from the instant its send was claimed (FR-5).</summary>
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);

    public required Guid Id { get; init; }

    /// <summary>The login's <c>sub</c> claim.</summary>
    public required string LoginSub { get; init; }

    /// <summary>The matched customer's ID in core banking, set when the OTP send is claimed; null on a blank challenge.</summary>
    public string? CustomerId { get; set; }

    /// <summary>HMAC-SHA256 of the challenge ID and the OTP under <c>Linking:OtpHmacKey</c>; null until an OTP is sent.</summary>
    public byte[]? OtpHmac { get; set; }

    /// <summary>Verification attempts so far; story 6.2 counts them.</summary>
    public int Attempts { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>When the OTP send was claimed; cleared again when the send failed.</summary>
    public DateTimeOffset? OtpSentAtUtc { get; set; }

    /// <summary>When a later start by the same login replaced this challenge.</summary>
    public DateTimeOffset? SupersededAtUtc { get; set; }

    /// <summary>When a correct OTP used this challenge; story 6.2 sets it.</summary>
    public DateTimeOffset? ConsumedAtUtc { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
