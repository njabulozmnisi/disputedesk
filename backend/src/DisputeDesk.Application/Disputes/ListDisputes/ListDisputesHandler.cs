using DisputeDesk.Application.Abstractions;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Disputes.ListDisputes;

/// <summary>
/// The current customer's disputes, newest first by <c>CreatedAtUtc</c> then <c>Id</c>, one keyset page at a time
/// (FR-16, AD-5; EF Core, Pagination: keyset pagination). Rows come from the <c>Disputes</c> read model through the
/// ownership filter (AD-11), so a cursor naming another customer's dispute is only a position. It never calls core
/// banking: the snapshot was stored at the raise (AD-21).
/// </summary>
public sealed class ListDisputesHandler(IAppDbContext db)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    // AD-3: the message never echoes the cursor.
    private static readonly Error InvalidCursor = Error.Validation("validation", "The cursor is not valid.", "Cursor");

    /// <param name="cursor">The previous page's <c>nextCursor</c>, or null for the first page.</param>
    /// <param name="limit">1 to <see cref="MaxPageSize"/>, already checked by built-in validation at the edge.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<Result<DisputePageResponse>> HandleAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        IQueryable<Dispute> disputes = db.Entities<Dispute>().AsNoTracking();
        if (cursor is not null)
        {
            var after = DisputeCursor.Decode(cursor);
            if (after is null)
            {
                return InvalidCursor;
            }

            // EF Core's keyset predicate, translated to SQL, so the uuid order is PostgreSQL's:
            // "CreatedAtUtc" < @at OR ("CreatedAtUtc" = @at AND "Id" < @id).
            DateTimeOffset at = after.CreatedAtUtc;
            Guid id = after.Id;
            disputes = disputes.Where(dispute => dispute.CreatedAtUtc < at || (dispute.CreatedAtUtc == at && dispute.Id.CompareTo(id) < 0));
        }

        // shortcut: no index serves this query; add IX_Disputes_CustomerId_CreatedAtUtc_Id when the list query's plan
        // (EXPLAIN) shows a sequential scan on Disputes

        // One row more than the page shows whether another page follows.
        List<Dispute> rows = await disputes
            .OrderByDescending(dispute => dispute.CreatedAtUtc)
            .ThenByDescending(dispute => dispute.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(limit);
        }

        string? nextCursor = hasMore ? DisputeCursor.Encode(rows[^1].CreatedAtUtc, rows[^1].Id) : null;
        return new DisputePageResponse([.. rows.Select(CustomerView.ToSummary)], nextCursor);
    }
}
