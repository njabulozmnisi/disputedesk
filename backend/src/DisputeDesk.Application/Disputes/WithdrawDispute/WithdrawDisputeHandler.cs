using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes.GetDispute;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Disputes.WithdrawDispute;

/// <summary>
/// The current customer withdraws one of their open disputes (FR-18). The dispute is loaded by replaying its stored
/// events through the ownership filter (AD-11, AD-13), so another customer's dispute is not found, exactly like an
/// unknown ID. The aggregate decides (400 for the note, then 409 for the status) and one commit writes the stored event,
/// its outbox row and the <c>Disputes</c> read model (AD-14). On a version clash it reloads and decides once more, and
/// on a second clash it answers 409 with the status as last read (AD-15). Refusals on rule or status (404, 409) are
/// audited as <c>Rejected</c> (AD-14a); a success writes no audit row (ad-009).
/// </summary>
public sealed class WithdrawDisputeHandler(IAppDbContext db, CurrentCustomer currentCustomer, TimeProvider time, GetDisputeHandler detail)
{
    private Actor Customer => new(ActorType.Customer, currentCustomer.LoginSub);

    public async Task<Result<DisputeResponse>> HandleAsync(
        Guid disputeId, WithdrawDisputeRequest request, CancellationToken cancellationToken)
    {
        // The one clock reading (AD-4), cut to the whole microseconds a Postgres timestamptz keeps, as the raise does.
        DateTimeOffset now = Instants.ToWholeMicroseconds(time.GetUtcNow());

        // A version clash is the commit refused by the unique stored event version; the commit has cleared the change
        // tracker (EF Core, Handling concurrency conflicts).
        try
        {
            return await WithdrawAsync(disputeId, request.Note, now, isRetry: false, cancellationToken);
        }
        catch (UniqueViolationException clash) when (clash.Error == DisputeErrors.VersionClash)
        {
            // AD-15: another change appended this version first. Replay the new stream and decide once more, never more.
        }

        try
        {
            return await WithdrawAsync(disputeId, request.Note, now, isRetry: true, cancellationToken);
        }
        catch (UniqueViolationException clash) when (clash.Error == DisputeErrors.VersionClash)
        {
            // A second clash: answered below from one read of the read model.
        }

        // One read of the read model and no further decision (AD-15, decided 2026-10-30). A change committed after this
        // read is not reflected. The refusal is decided, so, as for its audit save below, not the request's token: a
        // client that disconnects cannot cancel the record of its refusal (NFR-SEC-10).
        DisputeStatus lastRead = await db.Entities<Dispute>().AsNoTracking()
            .Where(dispute => dispute.Id == disputeId)
            .Select(dispute => dispute.Status)
            .SingleAsync(CancellationToken.None);
        return await RefuseAsync(DisputeWire.ActionNotAllowedAs(DisputeWire.ToCustomerStatus(lastRead)), disputeId, now);
    }

    /// <summary>
    /// Replays the dispute, decides and commits; a lost version clash throws <see cref="UniqueViolationException"/>. On the
    /// retry the instant is the later of <paramref name="now"/> and the stream's latest, so time never runs backwards
    /// within a stream (AD-15).
    /// </summary>
    private async Task<Result<DisputeResponse>> WithdrawAsync(
        Guid disputeId, string? note, DateTimeOffset now, bool isRetry, CancellationToken cancellationToken)
    {
        // Through the ownership filter (AD-11). A row of an unknown type throws: replay never builds a partial dispute (AD-13).
        List<StoredEventRow> rows = await db.Entities<StoredEventRow>().AsNoTracking()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return await RefuseAsync(DisputeErrors.NotFound, disputeId: null, now);
        }

        List<StoredEvent> stream = [.. rows.Select(row => row.ToStoredEvent())];
        var dispute = Dispute.Replay(stream);
        DateTimeOffset instant = isRetry ? Instants.Later(now, stream.Max(storedEvent => storedEvent.OccurredAtUtc)) : now;
        Result withdrawn = dispute.Withdraw(note, Customer, instant);
        if (withdrawn.Error == DisputeErrors.ActionNotAllowed)
        {
            return await RefuseAsync(DisputeWire.ActionNotAllowedAs(DisputeWire.ToCustomerStatus(dispute.Status)), dispute.Id, now);
        }

        if (withdrawn.IsFailure)
        {
            // The request's validation has already checked the note, so this guards the aggregate's own note rule alone:
            // without it a refusal would commit the read model with no stored event and answer 200. A validation error,
            // not a refusal on rule or status, so not audited (AD-14a).
            return withdrawn.Error;
        }

        // The replayed dispute is not tracked; Update marks every property modified, so the commit writes the read model
        // with the stored event (EF Core, Disconnected entities).
        db.Entities<Dispute>().Update(dispute);
        await db.SaveChangesAsync(cancellationToken);

        // The 200 is the dispute as the detail reads it back, so it equals a following GET.
        return await detail.HandleAsync(disputeId, cancellationToken);
    }

    /// <summary>Audits the refusal as <c>Rejected</c> through <see cref="AuditLog.Record"/> (AD-14a), saves it and returns it.</summary>
    private async Task<Result<DisputeResponse>> RefuseAsync(Error error, Guid? disputeId, DateTimeOffset now)
    {
        AuditLog.Record(db, AuditAction.Withdraw, AuditResult.Rejected, Customer, now, currentCustomer.CustomerId, disputeId);
        // Not the request's token, as linking saves its audit entries: a client that disconnects cannot cancel the record
        // of its refusal (NFR-SEC-10). The database command timeout still bounds the save.
        await db.SaveChangesAsync(CancellationToken.None);
        return error;
    }
}
