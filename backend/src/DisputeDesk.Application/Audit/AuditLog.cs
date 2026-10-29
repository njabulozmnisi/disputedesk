using System.Diagnostics;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Audit;

/// <summary>The one way to write an audit entry (AD-14a): sign-in and sign-out, linking, refused actions and operations reads.</summary>
public static class AuditLog
{
    /// <summary>
    /// Adds one <c>AuditLogEntries</c> row, stamped with the caller's instant (AD-4) and the current trace. The caller
    /// saves it.
    /// </summary>
    public static void Record(
        IAppDbContext db,
        AuditAction action,
        AuditResult result,
        Actor actor,
        DateTimeOffset now,
        string? customerId = null,
        Guid? disputeId = null)
    {
        db.Entities<AuditLogEntry>().Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(now),
            OccurredAtUtc = now,
            Action = action,
            Result = result,
            ActorType = actor.Type,
            ActorId = actor.Id,
            CustomerId = customerId,
            DisputeId = disputeId,
            TraceId = Activity.Current?.TraceId.ToHexString(),
        });
    }
}
