using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DisputeDesk.Application.Linking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// Locking a customer's sends, counting them, claiming one and releasing it after a failed send (AD-12; decided
/// 2026-10-30). The claim and the release are each one conditional <c>UPDATE</c> that reports how many rows it changed
/// (EF Core, ExecuteUpdate and ExecuteDelete: concurrency control and rows affected). The claim and the send are two
/// steps, not one atomic operation.
/// </summary>
public static class OtpSends
{
    /// <summary>
    /// The first key of the customer lock. Any fixed value: the two-key lock space never overlaps the one-key space the
    /// outbox relay locks in (PostgreSQL, Advisory Lock Functions).
    /// </summary>
    private const int CustomerLockNamespace = 6_005;

    /// <summary>
    /// Takes the transaction-level advisory lock on <paramref name="customerId"/>, waiting while another job holds it;
    /// Postgres releases it when the transaction ends. Must run inside a transaction, as its first statement.
    /// </summary>
    /// <remarks>
    /// Raw SQL because EF Core has no API for advisory locks. Parameterised: <c>ExecuteSqlAsync</c> turns each
    /// interpolated value into a parameter. The second key is the first 4 bytes of the SHA-256 of the customer ID, a
    /// stable 32-bit value in every process (<c>string.GetHashCode</c> is not); two customers sharing it only wait for
    /// each other.
    /// </remarks>
    public static Task LockCustomerAsync(this DatabaseFacade database, string customerId, CancellationToken cancellationToken)
    {
        int customerKey = BinaryPrimitives.ReadInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(customerId)));
        return database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CustomerLockNamespace}, {customerKey})", cancellationToken);
    }

    /// <summary>The customer's claimed sends, delivered or not, stamped after <paramref name="after"/>.</summary>
    public static Task<int> CountSendsAfterAsync(
        this DbSet<LinkingChallenge> challenges, string customerId, DateTimeOffset after, CancellationToken cancellationToken) =>
        // shortcut: no index for the cap count, add one on (CustomerId, OtpSentAtUtc) when LinkingChallenges outgrows a
        // sequential scan
        challenges.CountAsync(challenge => challenge.CustomerId == customerId && challenge.OtpSentAtUtc > after, cancellationToken);

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
    /// Clears this job's own OTP after its send failed, so it starts no lifetime and can never verify. Keeps
    /// <c>OtpSentAtUtc</c>, so the claim still counts toward the hourly cap (decided 2026-10-30). Matches on the claimed
    /// instant, so it never clears another job's claim. Runs to completion whatever is being cancelled, so a claim is
    /// never left with a live OTP by a cancelled release.
    /// </summary>
    public static Task<int> ReleaseAsync(this DbSet<LinkingChallenge> challenges, Guid challengeId, DateTimeOffset sentAt) =>
        challenges
            .Where(challenge => challenge.Id == challengeId && challenge.OtpSentAtUtc == sentAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(challenge => challenge.OtpHmac, (byte[]?)null)
                    .SetProperty(challenge => challenge.ExpiresAtUtc, (DateTimeOffset?)null),
                CancellationToken.None);
}
