using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes.GetDispute;
using DisputeDesk.Contracts.Audit;
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
    // AD-3: the message never echoes the ID.
    private static readonly Error NotFound = Error.NotFound("not-found", "There is no such dispute.");

    private Actor Customer => new(ActorType.Customer, currentCustomer.LoginSub);

    public async Task<Result<Contracts.Disputes.DisputeResponse>> HandleAsync(
        Guid disputeId, Contracts.Disputes.WithdrawDisputeRequest request, CancellationToken cancellationToken)
    {
        // The one clock reading (AD-4), cut to the whole microseconds a Postgres timestamptz keeps, as the raise does.
        DateTimeOffset now = ToWholeMicroseconds(time.GetUtcNow());

        Result<Contracts.Disputes.DisputeResponse>? first = await TryWithdrawAsync(disputeId, request.Note, now, isRetry: false, cancellationToken);
        if (first is not null)
        {
            return first;
        }

        // AD-15: another change appended this version first. Replay the new stream and decide once more, never more.
        Result<Contracts.Disputes.DisputeResponse>? retry = await TryWithdrawAsync(disputeId, request.Note, now, isRetry: true, cancellationToken);
        if (retry is not null)
        {
            return retry;
        }

        // A second clash: one read of the read model and no further decision (AD-15, decided 2026-10-30). A change
        // committed after this read is not reflected.
        DisputeStatus lastRead = await db.Entities<Dispute>().AsNoTracking()
            .Where(dispute => dispute.Id == disputeId)
            .Select(dispute => dispute.Status)
            .SingleAsync(cancellationToken);
        return await RefuseAsync(DisputeErrors.ActionNotAllowedAs(DisputeWire.ToCustomerStatus(lastRead)), disputeId, now, cancellationToken);
    }

    /// <summary>
    /// Replays the dispute, decides and commits; null when the commit lost a version clash, after which the override has
    /// cleared the change tracker. On the retry the instant is the later of <paramref name="now"/> and the stream's
    /// latest, so time never runs backwards within a stream (AD-15).
    /// </summary>
    private async Task<Result<Contracts.Disputes.DisputeResponse>?> TryWithdrawAsync(
        Guid disputeId, string? note, DateTimeOffset now, bool isRetry, CancellationToken cancellationToken)
    {
        Dispute? dispute = await db.LoadDisputeAsync(disputeId, cancellationToken);
        if (dispute is null)
        {
            return await RefuseAsync(NotFound, disputeId: null, now, cancellationToken);
        }

        DateTimeOffset instant = isRetry ? Later(now, await LatestOccurredAtAsync(disputeId, cancellationToken)) : now;
        Result withdrawn = dispute.Withdraw(note, Customer, instant);
        if (withdrawn.Error == DisputeErrors.ActionNotAllowed)
        {
            return await RefuseAsync(DisputeErrors.ActionNotAllowedAs(DisputeWire.ToCustomerStatus(dispute.Status)), dispute.Id, now, cancellationToken);
        }

        if (withdrawn.IsFailure)
        {
            // The note's own rule: a validation error, not a refusal on rule or status, so not audited (AD-14a).
            return withdrawn.Error;
        }

        // The replayed dispute is not tracked; Update marks every property modified, so the commit writes the read model
        // with the stored event (EF Core, Disconnected entities).
        db.Entities<Dispute>().Update(dispute);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueViolationException clash) when (clash.Error == DisputeErrors.VersionClash)
        {
            return null;
        }

        // The 200 is the dispute as the detail reads it back, so it equals a following GET.
        return await detail.HandleAsync(disputeId, cancellationToken);
    }

    /// <summary>Audits the refusal as <c>Rejected</c> through <see cref="AuditLog.Record"/> (AD-14a), saves it and returns it.</summary>
    private async Task<Result<Contracts.Disputes.DisputeResponse>> RefuseAsync(Error error, Guid? disputeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        AuditLog.Record(db, AuditAction.Withdraw, AuditResult.Rejected, Customer, now, currentCustomer.CustomerId, disputeId);
        await db.SaveChangesAsync(cancellationToken);
        return error;
    }

    private Task<DateTimeOffset> LatestOccurredAtAsync(Guid disputeId, CancellationToken cancellationToken) =>
        db.Entities<StoredEventRow>()
            .Where(row => row.DisputeId == disputeId)
            .MaxAsync(row => row.OccurredAtUtc, cancellationToken);

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    // A Postgres timestamptz keeps whole microseconds (PostgreSQL, Date/Time Types); a DateTimeOffset has 100 ns ticks.
    private static DateTimeOffset ToWholeMicroseconds(DateTimeOffset instant) =>
        instant.AddTicks(-(instant.Ticks % TimeSpan.TicksPerMicrosecond));
}
