using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Linking;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Application.Linking.StartLinking;

/// <summary>
/// Starts linking for an unlinked login (FR-4, AD-12). Built-in validation has already checked the format. Whatever the
/// details are, it does the same writes and nothing else: it supersedes the login's current challenge, inserts a blank
/// one and records <c>LinkingStart</c>, in one save, then queues the job that checks the details off the request path.
/// The request never calls core banking, the SIM-swap check or the SMS sender, so neither its response nor its timing
/// can tell whether the details matched (OWASP Forgot Password Cheat Sheet). A full queue answers
/// <see cref="LinkingErrors.QueueFull"/> to every start alike.
/// </summary>
public sealed partial class StartLinkingHandler(
    IAppDbContext db, TimeProvider time, ChannelWriter<LinkingJob> jobs, ILogger<StartLinkingHandler> logger)
{
    public async Task<Result> HandleAsync(StartLinkingRequest request, string loginSub, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        // The one clock reading (AD-4).
        DateTimeOffset now = time.GetUtcNow();

        LinkingChallenge? current = await db.Entities<LinkingChallenge>().SingleOrDefaultAsync(
            challenge => challenge.LoginSub == loginSub && challenge.SupersededAtUtc == null && challenge.ConsumedAtUtc == null,
            cancellationToken);
        if (current is not null)
        {
            current.SupersededAtUtc = now;
        }

        var challenge = new LinkingChallenge { Id = Guid.CreateVersion7(now), LoginSub = loginSub, Attempts = 0, CreatedAtUtc = now };
        db.Entities<LinkingChallenge>().Add(challenge);
        AuditLog.Record(db, AuditAction.LinkingStart, AuditResult.Succeeded, new Actor(ActorType.Customer, loginSub), now, clientIp: clientIp);
        try
        {
            // One save, so one transaction (EF Core, Transactions). EF Core sends the update of the superseded challenge
            // before the insert, so the filtered unique index never sees two current challenges of one login.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueViolationException violation) when (violation.Error == LinkingErrors.ChallengeClash)
        {
            // A parallel start by the same login committed first. Nothing of this one was written, and that start's job
            // does the checks, so this one gets the same 202 and queues nothing.
            return Result.Success();
        }

        // The challenge is saved before the job is queued, so a job never runs ahead of its row.
        var job = new LinkingJob(challenge.Id, loginSub, clientIp, Activity.Current?.Context ?? default, request.IdNumber, request.AccountNumber);
        if (jobs.TryWrite(job))
        {
            return Result.Success();
        }

        // A full bounded channel refuses the write rather than wait (System.Threading.Channels, bounded channels). The
        // blank challenge stays current and unclaimed, so it can only ever verify as a wrong code.
        await RecordQueueFullAsync(challenge.Id, loginSub, clientIp, now);
        return LinkingErrors.QueueFull;
    }

    /// <summary>
    /// Records <c>LinkingRateLimited</c> in a second save, which runs to completion whatever is cancelled. A failed save
    /// is logged with only the challenge ID and the action; the start still answers the same 429.
    /// </summary>
    private async Task RecordQueueFullAsync(Guid challengeId, string loginSub, IPAddress? clientIp, DateTimeOffset now)
    {
        AuditLog.Record(db, AuditAction.LinkingRateLimited, AuditResult.Rejected, new Actor(ActorType.Customer, loginSub), now, clientIp: clientIp);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The exception is not logged: a database error can quote the row, which holds the login and the address.
            LogAuditFailed(logger, challengeId, AuditAction.LinkingRateLimited);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the {Action} audit entry of linking challenge {ChallengeId} failed")]
    private static partial void LogAuditFailed(ILogger logger, Guid challengeId, AuditAction action);
}
