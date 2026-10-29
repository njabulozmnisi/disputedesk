using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// Loading a dispute from its stream (ad-009). It reads <c>StoredEvents</c> through the ownership filter (AD-11), so
/// another customer's dispute and an unknown ID look alike.
/// </summary>
public static class DisputeReplay
{
    /// <summary>
    /// Loads the dispute by replaying its stream (AD-13), or null when no stream is visible (404). A malformed stream, or
    /// a row of an unknown type, throws: it is never a partial dispute or not found.
    /// </summary>
    public static async Task<Dispute?> LoadDisputeAsync(this IAppDbContext db, Guid disputeId, CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredEvent> storedEvents = await ReadStoredEventsAsync(db, disputeId, cancellationToken);
        return storedEvents.Count == 0 ? null : Dispute.Replay(storedEvents);
    }

    /// <summary>
    /// The dispute's stored events in <see cref="StoredEvent.Version"/> order; empty when none is visible. A row of an
    /// unknown type throws, which only replay wants: the customer view skips such a row (AD-13).
    /// </summary>
    private static async Task<IReadOnlyList<StoredEvent>> ReadStoredEventsAsync(IAppDbContext db, Guid disputeId, CancellationToken cancellationToken)
    {
        List<StoredEventRow> rows = await db.Entities<StoredEventRow>().AsNoTracking()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => row.ToStoredEvent())];
    }
}
