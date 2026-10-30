using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Operations;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Operations.Actions;

/// <summary>
/// Operations start review, escalate or resolve a dispute (FR-21 to FR-24). Like the customer's withdraw: the dispute is
/// loaded by replaying its stream (AD-13), here through the named ownership bypass, because operations act for no
/// customer (AD-11); the aggregate decides; one commit writes the stored event, its outbox row and the <c>Disputes</c> read
/// model (AD-14). On a version clash it replays and decides once more, and on a second clash it answers 409 with the
/// status as last read (AD-15). Refusals on rule or status (404, 409) are audited as <c>Rejected</c> (AD-14a); a success
/// writes no audit row (ad-009).
/// </summary>
public sealed class OperationsActionHandler(IAppDbContext db, CurrentOperationsActor operationsActor, TimeProvider time)
{
    // AD-3: the message never echoes the ID. The same error as the read, so the 404 bodies match.
    private static readonly Error NotFound = Error.NotFound("not-found", "There is no such dispute.");

    /// <param name="disputeId">The dispute to act on.</param>
    /// <param name="action">Start review, escalate or resolve.</param>
    /// <param name="resolve">The resolve's body; null for the other two actions.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<Result<OperationsActionResult>> HandleAsync(
        Guid disputeId, DisputeAction action, ResolveDisputeRequest? resolve, CancellationToken cancellationToken)
    {
        // The one clock reading (AD-4), cut to the whole microseconds a Postgres timestamptz keeps, as the withdraw does.
        DateTimeOffset now = ToWholeMicroseconds(time.GetUtcNow());

        Result<OperationsActionResult>? first = await TryActAsync(disputeId, action, resolve, now, isRetry: false, cancellationToken);
        if (first is not null)
        {
            return first;
        }

        // AD-15: another change appended this version first. Replay the new stream and decide once more, never more.
        Result<OperationsActionResult>? retry = await TryActAsync(disputeId, action, resolve, now, isRetry: true, cancellationToken);
        if (retry is not null)
        {
            return retry;
        }

        // A second clash: one read of the read model and no further decision (AD-15). A change committed after this read
        // is not reflected.
        DisputeStatus lastRead = await db.Entities<Dispute>().IgnoreQueryFilters(["Ownership"]).AsNoTracking()
            .Where(dispute => dispute.Id == disputeId)
            .Select(dispute => dispute.Status)
            .SingleAsync(cancellationToken);
        return await RefuseAsync(NotAllowedIn(lastRead), action, disputeId, now, cancellationToken);
    }

    /// <summary>
    /// Replays the dispute, decides and commits; null when the commit lost a version clash, after which the commit has
    /// cleared the change tracker. On the retry the instant is the later of <paramref name="now"/> and the stream's
    /// latest, so time never runs backwards within a stream (AD-15).
    /// </summary>
    private async Task<Result<OperationsActionResult>?> TryActAsync(
        Guid disputeId, DisputeAction action, ResolveDisputeRequest? resolve, DateTimeOffset now, bool isRetry, CancellationToken cancellationToken)
    {
        // "Ownership" is AppDbContext.OwnershipFilter, which Application may not reference (LayerTests). Only the named
        // filter is ignored (EF Core, Global query filters: named filters). A row of an unknown type throws: replay never
        // builds a partial dispute (AD-13).
        List<StoredEventRow> rows = await db.Entities<StoredEventRow>().IgnoreQueryFilters(["Ownership"]).AsNoTracking()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return await RefuseAsync(NotFound, action, disputeId, now, cancellationToken);
        }

        List<StoredEvent> stream = [.. rows.Select(row => row.ToStoredEvent())];
        var dispute = Dispute.Replay(stream);
        DateTimeOffset instant = isRetry ? Later(now, stream.Max(storedEvent => storedEvent.OccurredAtUtc)) : now;
        Result decided = Decide(dispute, action, resolve, instant);
        if (decided.Error == DisputeErrors.ActionNotAllowed)
        {
            return await RefuseAsync(NotAllowedIn(dispute.Status), action, dispute.Id, now, cancellationToken);
        }

        if (decided.IsFailure)
        {
            // The body's own rules: a validation error, not a refusal on rule or status, so not audited (AD-14a).
            return decided.Error;
        }

        // The commit takes the pending stored events, so keep them for the response.
        stream.AddRange(dispute.PendingChanges);

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

        // Built from the dispute just committed rather than read back, so a success writes no DisputeRead entry.
        return new OperationsActionResult(OperationsView.ToResponse(dispute, stream), WasEscalatedBySlaSweep(stream));
    }

    private Result Decide(Dispute dispute, DisputeAction action, ResolveDisputeRequest? resolve, DateTimeOffset instant)
    {
        switch (action)
        {
            case DisputeAction.StartReview:
                return dispute.StartReview(operationsActor.Actor, instant);
            case DisputeAction.Escalate:
                return dispute.Escalate(operationsActor.Actor, instant);
            case DisputeAction.Resolve:
                // The outcome before the note and the status, all after the lookup (AD-8).
                if (resolve?.Outcome is not Contracts.Disputes.Outcome outcome)
                {
                    return DisputeErrors.OutcomeRequired;
                }

                return dispute.Resolve(DisputeWire.ToDomain(outcome), resolve.Note, operationsActor.Actor, instant);
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Not an operations action.");
        }
    }

    /// <summary>
    /// Audits the refusal as <c>Rejected</c> through <see cref="AuditLog.Record"/> (AD-14a), saves it and returns it. No
    /// customer ID and no outcome: FR-25 names the actor, the action and the result.
    /// </summary>
    private async Task<Result<OperationsActionResult>> RefuseAsync(
        Error error, DisputeAction action, Guid disputeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        AuditLog.Record(db, ToAuditAction(action), AuditResult.Rejected, operationsActor.Actor, now, disputeId: disputeId);
        await db.SaveChangesAsync(cancellationToken);
        return error;
    }

    private static Error NotAllowedIn(DisputeStatus status) => DisputeErrors.ActionNotAllowedWithDisputeStatus(DisputeWire.ToWire(status));

    private static AuditAction ToAuditAction(DisputeAction action) => action switch
    {
        DisputeAction.StartReview => AuditAction.StartReview,
        DisputeAction.Escalate => AuditAction.Escalate,
        DisputeAction.Resolve => AuditAction.Resolve,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not an operations action."),
    };

    /// <summary>Whether the SLA sweep escalated the dispute; a manual escalation does not count (SM-4).</summary>
    private static bool WasEscalatedBySlaSweep(IEnumerable<StoredEvent> stream) =>
        stream.Any(storedEvent => storedEvent is DisputeEscalated && storedEvent.ActorId == Actor.SlaSweepId);

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    // A Postgres timestamptz keeps whole microseconds (PostgreSQL, Date/Time Types); a DateTimeOffset has 100 ns ticks.
    private static DateTimeOffset ToWholeMicroseconds(DateTimeOffset instant) =>
        instant.AddTicks(-(instant.Ticks % TimeSpan.TicksPerMicrosecond));
}
