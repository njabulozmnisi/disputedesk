using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Infrastructure.Sla;

/// <summary>
/// The SLA sweep's claim (AD-18). It locks up to <see cref="BatchSize"/> due <c>Disputes</c> rows for the caller's
/// transaction with <c>FOR UPDATE SKIP LOCKED</c>, so two sweeps never claim the same row and neither waits on a row the
/// other holds (PostgreSQL, SELECT, The Locking Clause). EF Core's LINQ has no locking clause, so it is one raw-SQL
/// query, parameterised through <c>Database.SqlQuery</c>'s interpolation (EF Core, SQL Queries). It returns IDs only:
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
}
