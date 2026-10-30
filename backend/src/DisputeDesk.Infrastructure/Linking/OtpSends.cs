using DisputeDesk.Application.Linking;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// Claiming an OTP send and releasing it after a failed send (AD-12; decided 2026-10-30). Each is one conditional
/// <c>UPDATE</c> that commits on its own and reports how many rows it changed (EF Core, ExecuteUpdate and
/// ExecuteDelete: concurrency control and rows affected). The claim and the send are two steps, not one atomic operation.
/// </summary>
public static class OtpSends
{
    /// <summary>
    /// Fills the challenge for the matched customer and stamps the send at <paramref name="sentAt"/>, only while it is
    /// still current and unsent. Returns 1 when this job may send, 0 when a later start superseded it.
    /// </summary>
    public static Task<int> ClaimAsync(
        this DbSet<LinkingChallenge> challenges, Guid challengeId, string customerId, byte[] otpHmac, DateTimeOffset sentAt, CancellationToken cancellationToken) =>
        challenges
            .Where(challenge => challenge.Id == challengeId
                && challenge.SupersededAtUtc == null
                && challenge.ConsumedAtUtc == null
                && challenge.OtpSentAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(challenge => challenge.CustomerId, customerId)
                    .SetProperty(challenge => challenge.OtpHmac, otpHmac)
                    .SetProperty(challenge => challenge.OtpSentAtUtc, sentAt)
                    .SetProperty(challenge => challenge.ExpiresAtUtc, sentAt + LinkingChallenge.OtpLifetime),
                cancellationToken);

    /// <summary>
    /// Clears this job's own claim after its send failed, so it counts nothing toward the hourly cap and starts no
    /// lifetime. Matches on the claimed instant, so it never clears another job's claim. Runs to completion whatever is
    /// being cancelled, so a claim is never left stamped by a cancelled release.
    /// </summary>
    public static Task<int> ReleaseAsync(this DbSet<LinkingChallenge> challenges, Guid challengeId, DateTimeOffset sentAt) =>
        challenges
            .Where(challenge => challenge.Id == challengeId && challenge.OtpSentAtUtc == sentAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(challenge => challenge.OtpHmac, (byte[]?)null)
                    .SetProperty(challenge => challenge.OtpSentAtUtc, (DateTimeOffset?)null)
                    .SetProperty(challenge => challenge.ExpiresAtUtc, (DateTimeOffset?)null),
                CancellationToken.None);
}
