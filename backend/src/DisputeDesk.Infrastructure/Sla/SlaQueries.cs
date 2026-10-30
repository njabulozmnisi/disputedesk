using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Infrastructure.Sla;

/// <summary>
/// The SLA sweep's two claims (AD-18). Each locks up to <see cref="BatchSize"/> due <c>Disputes</c> rows for the caller's
/// transaction with <c>FOR UPDATE SKIP LOCKED</c>, so two sweeps never claim the same row and neither waits on a row the
/// other holds (PostgreSQL, SELECT, The Locking Clause). EF Core's LINQ has no locking clause, so each is one raw-SQL
/// query, parameterised through <c>Database.SqlQuery</c>'s interpolation (EF Core, SQL Queries). They return IDs only:
/// <c>SqlQuery</c> applies no query filter, so no ownership bypass is needed here (AD-11).
/// </summary>
public static class SlaQueries
{
    /// <summary>The most disputes one claim locks, and so one batch changes (AD-18).</summary>
    public const int BatchSize = 50;

    /// <summary>
    /// Claims the open disputes whose SLA is due at or before <paramref name="now"/>, earliest first, on the partial index
    /// <c>IX_Disputes_SlaDueAtUtc</c>. Call it inside a transaction: the locks last until that transaction ends.
    /// </summary>
    public static Task<List<Guid>> ClaimOverdueAsync(this AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<Guid>(
            $"""
            SELECT "Id" AS "Value"
            FROM "Disputes"
            WHERE "Status" IN ('Submitted', 'UnderReview') AND "SlaDueAtUtc" <= {now}
            ORDER BY "SlaDueAtUtc"
            LIMIT {BatchSize}
            FOR UPDATE SKIP LOCKED
            """)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Claims the open disputes not yet warned whose SLA is due after <paramref name="now"/> and at most
    /// <paramref name="leadTime"/> after it, earliest first, on the partial index <c>IX_Disputes_SlaWarning</c>. Call it
    /// inside a transaction: the locks last until that transaction ends. The end of the window is computed here, in .NET,
    /// and sent as one <c>timestamptz</c> parameter, so no interval arithmetic runs in SQL.
    /// </summary>
    public static Task<List<Guid>> ClaimWarningsAsync(this AppDbContext db, DateTimeOffset now, TimeSpan leadTime, CancellationToken cancellationToken)
    {
        DateTimeOffset warnUntil = now + leadTime;
        return db.Database.SqlQuery<Guid>(
            $"""
            SELECT "Id" AS "Value"
            FROM "Disputes"
            WHERE "SlaWarningRaisedAtUtc" IS NULL AND "Status" IN ('Submitted', 'UnderReview')
              AND "SlaDueAtUtc" > {now} AND "SlaDueAtUtc" <= {warnUntil}
            ORDER BY "SlaDueAtUtc"
            LIMIT {BatchSize}
            FOR UPDATE SKIP LOCKED
            """)
            .ToListAsync(cancellationToken);
    }
}
