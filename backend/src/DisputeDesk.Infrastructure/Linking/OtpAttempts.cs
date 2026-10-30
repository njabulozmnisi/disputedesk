using DisputeDesk.Application.Linking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>The statements of an OTP verification (AD-12): count the attempt, then, for a correct code, consume the challenge.</summary>
public static class OtpAttempts
{
    /// <summary>
    /// Counts one attempt on the login's current challenge while it has attempts left, in one statement that commits on
    /// its own, and returns the row as it now is, or null when the login has no current challenge or it is locked.
    /// Every kind of challenge (claimed, blank, released, expired) counts the same way.
    /// </summary>
    /// <remarks>
    /// Raw SQL because the handler needs the row the increment changed, read in the same statement: EF Core's
    /// <c>ExecuteUpdate</c> returns only the number of rows, and a read then a write would let parallel attempts both pass
    /// the limit. Postgres's <c>UPDATE ... RETURNING</c> does both atomically under the row lock. <c>SqlQuery</c> keeps it
    /// parameterised (EF Core, SQL queries: querying unmapped types). Not composed on, because an UPDATE cannot be a
    /// subquery.
    /// </remarks>
    public static async Task<CountedAttempt?> CountAttemptAsync(this DatabaseFacade database, string loginSub, CancellationToken cancellationToken)
    {
        List<CountedAttempt> counted = await database.SqlQuery<CountedAttempt>($"""
            UPDATE "LinkingChallenges"
            SET "Attempts" = "Attempts" + 1
            WHERE "LoginSub" = {loginSub}
              AND "SupersededAtUtc" IS NULL
              AND "ConsumedAtUtc" IS NULL
              AND "Attempts" < {LinkingChallenge.MaxAttempts}
            RETURNING "Id", "CustomerId", "OtpHmac", "ExpiresAtUtc", "Attempts"
            """).ToListAsync(cancellationToken);

        return counted.SingleOrDefault();
    }

    /// <summary>
    /// Marks the challenge consumed at <paramref name="now"/>, only while it is still current. Returns 1 when this
    /// verification may link, 0 when another verification consumed it or a later start superseded it (EF Core,
    /// ExecuteUpdate and ExecuteDelete: concurrency control and rows affected).
    /// </summary>
    public static Task<int> ConsumeAsync(this DbSet<LinkingChallenge> challenges, Guid challengeId, DateTimeOffset now, CancellationToken cancellationToken) =>
        challenges
            .Where(challenge => challenge.Id == challengeId && challenge.SupersededAtUtc == null && challenge.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(challenge => challenge.ConsumedAtUtc, now), cancellationToken);
}
