using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Linking;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Application.Linking.StartLinking;

/// <summary>
/// Starts linking for an unlinked login (FR-4, AD-12). Built-in validation has already checked the format. Whatever the
/// details are, it does the same writes and nothing else: it supersedes the login's current challenge, inserts a blank
/// one and records <c>LinkingStart</c>, in one save, then queues the job that checks the details off the request path.
/// The request never calls core banking, the SIM-swap check or the SMS sender, so neither its response nor its timing
/// can tell whether the details matched (OWASP Forgot Password Cheat Sheet).
/// </summary>
public sealed partial class StartLinkingHandler(
    IAppDbContext db, TimeProvider time, ChannelWriter<LinkingJob> jobs, ILogger<StartLinkingHandler> logger)
{
    public async Task HandleAsync(StartLinkingRequest request, string loginSub, IPAddress? clientIp, CancellationToken cancellationToken)
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
            return;
        }

        var job = new LinkingJob(challenge.Id, loginSub, clientIp, Activity.Current?.Context ?? default, request.IdNumber, request.AccountNumber);
        if (!jobs.TryWrite(job))
        {
            // shortcut: a full queue answers 202, until story 6.5 adds the audited 429
            LogQueueFull(logger, challenge.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The linking queue is full; challenge {ChallengeId} was not queued")]
    private static partial void LogQueueFull(ILogger logger, Guid challengeId);
}
