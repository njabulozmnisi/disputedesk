using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Disputes.GetDispute;

/// <summary>
/// One of the current customer's disputes as they see it (FR-17): the dispute fields from the <c>Disputes</c> read
/// model and the timeline from its stored events, through the same <see cref="CustomerView"/> as the raise 201 (AD-13).
/// Both reads go through the ownership filter (AD-11), so another customer's dispute is not found, exactly like an
/// unknown ID. It never calls core banking: the snapshot was stored at the raise (AD-21).
/// </summary>
public sealed class GetDisputeHandler(IAppDbContext db)
{
    // AD-3: the message never echoes the ID.
    private static readonly Error NotFound = Error.NotFound("not-found", "There is no such dispute.");

    public async Task<Result<Contracts.Disputes.DisputeResponse>> HandleAsync(Guid disputeId, CancellationToken cancellationToken)
    {
        Dispute? dispute = await db.Entities<Dispute>().AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == disputeId, cancellationToken);
        if (dispute is null)
        {
            return NotFound;
        }

        // shortcut: the row and the stream are two statements under READ COMMITTED, so a change committing between them
        // can show a timeline one entry ahead of the status until the next read; read both in one REPEATABLE READ
        // transaction if that ever matters
        List<StoredEventRow> rows = await db.Entities<StoredEventRow>().AsNoTracking()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(cancellationToken);

        // A row of a type this code does not know is left out before its data is read, never an error (AD-13).
        IEnumerable<StoredEvent> storedEvents = rows.Where(row => row.HasKnownEventType).Select(row => row.ToStoredEvent());
        return CustomerView.ToResponse(dispute, storedEvents);
    }
}
