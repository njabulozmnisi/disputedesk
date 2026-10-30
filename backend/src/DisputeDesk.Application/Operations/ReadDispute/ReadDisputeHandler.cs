using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Operations;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Operations.ReadDispute;

/// <summary>
/// One dispute as operations see it (FR-30): the dispute fields from the <c>Disputes</c> read model and the timeline from
/// every stored event, through <see cref="OperationsView"/>. Operations act for no customer, so both reads bypass the
/// ownership filter by name (AD-11). Every read is audited as <c>DisputeRead</c> (AD-14a; NFR-SEC-10), and the entry is
/// saved before the body is returned: a read that cannot be audited fails with nothing returned (fail closed, as the
/// sign-in does; OWASP Logging Cheat Sheet).
/// </summary>
public sealed class ReadDisputeHandler(IAppDbContext db, CurrentOperationsActor operationsActor, TimeProvider time)
{
    // AD-3: the message never echoes the ID. The same error as the customer detail, so the 404 bodies match (AD-11).
    private static readonly Error NotFound = Error.NotFound("not-found", "There is no such dispute.");

    public async Task<Result<OperationsDisputeResponse>> HandleAsync(Guid disputeId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        // "Ownership" is AppDbContext.OwnershipFilter, which Application may not reference (LayerTests). Only the named
        // filter is ignored, never every filter (EF Core, Global query filters: named filters).
        Dispute? dispute = await db.Entities<Dispute>().IgnoreQueryFilters(["Ownership"]).AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == disputeId, cancellationToken);
        if (dispute is null)
        {
            AuditLog.Record(db, AuditAction.DisputeRead, AuditResult.Rejected, operationsActor.Actor, now, disputeId: disputeId);
            await db.SaveChangesAsync(cancellationToken);
            return NotFound;
        }

        // shortcut: the row and the stream are two statements under READ COMMITTED, as in the customer detail, so a change
        // committing between them can show a timeline one entry ahead of the status; read both in one REPEATABLE READ
        // transaction if that ever matters
        List<StoredEventRow> rows = await db.Entities<StoredEventRow>().IgnoreQueryFilters(["Ownership"]).AsNoTracking()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(cancellationToken);

        // A row of a type this code does not know is left out before its data is read, never an error (AD-13).
        IEnumerable<StoredEvent> storedEvents = rows.Where(row => row.HasKnownEventType).Select(row => row.ToStoredEvent());
        OperationsDisputeResponse response = OperationsView.ToResponse(dispute, storedEvents);

        // No customer ID: FR-25 names only the actor and the dispute.
        AuditLog.Record(db, AuditAction.DisputeRead, AuditResult.Succeeded, operationsActor.Actor, now, disputeId: dispute.Id);
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }
}
