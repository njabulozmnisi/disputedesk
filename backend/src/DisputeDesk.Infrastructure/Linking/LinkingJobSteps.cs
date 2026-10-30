using System.Diagnostics;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// One linking job, off the request path (AD-12), in this order: the core-banking match, the link check, the SIM-swap
/// check on the cellphone on the bank's record (failing closed), then the claim, the send and, when the send fails, the
/// release. Every stop records one audit entry with the login, the client IP and the job's trace. Scoped: one instance,
/// with its own database context, per job.
/// </summary>
public sealed partial class LinkingJobSteps(
    AppDbContext db,
    ICoreBankingClient coreBanking,
    ISimSwapCheck simSwap,
    ISmsSender sms,
    TimeProvider time,
    IOptions<LinkingOptions> options,
    ILogger<LinkingJobSteps> logger)
{
    private static readonly ActivitySource Source = new(Telemetry.ActivitySourceName);

    /// <summary>
    /// Runs the job to its one stop. Only a host shutdown before the claim ends it without an audit entry (an Api restart
    /// drops queued jobs, AD-12).
    /// </summary>
    public async Task RunAsync(LinkingJob job, CancellationToken stoppingToken)
    {
        // A new trace linked to the request's, not a child of it: the request has long answered (OpenTelemetry, Links
        // between spans). Its trace ID goes on every audit entry of the job.
        using Activity? span = Source.StartActivity(
            "linking job", ActivityKind.Internal, parentContext: default, links: [new ActivityLink(job.RequestContext)]);
        // The one clock reading of the job (AD-4). The claim and the release pass this same instant, which Npgsql stores
        // at the same whole microseconds both times, so the release matches the claim.
        DateTimeOffset now = time.GetUtcNow();

        CustomerMatch? match = await CheckBeforeClaimAsync(job, span, now, stoppingToken);
        if (match is not null)
        {
            await ClaimAndSendAsync(job, span, match, now, stoppingToken);
        }
    }

    /// <summary>
    /// The checks, within the job's budget (AD-12). Returns the match when an OTP may be sent, otherwise records the stop
    /// and returns null.
    /// </summary>
    private async Task<CustomerMatch?> CheckBeforeClaimAsync(LinkingJob job, Activity? span, DateTimeOffset now, CancellationToken stoppingToken)
    {
        // Real time, not the injected clock: a test clock never advances on its own.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        budget.CancelAfter(options.Value.Budget);
        string? customerId = null;
        try
        {
            Result<CustomerMatch> matched = await coreBanking.MatchCustomerAsync(job.IdNumber, job.AccountNumber, budget.Token);
            if (matched.IsFailure)
            {
                if (matched.Error == CoreBankingErrors.NotFound)
                {
                    await RecordAsync(job, span, now, AuditAction.LinkingNoMatch, AuditResult.Rejected, customerId: null);
                    return null;
                }

                await RecordAsync(job, span, now, AuditAction.LinkingStart, AuditResult.Failed, customerId: null);
                return null;
            }

            CustomerMatch match = matched.Value;
            customerId = match.CustomerId;
            bool isLinked = await db.Set<CustomerLink>().AnyAsync(link => link.CustomerId == match.CustomerId, budget.Token);
            if (isLinked)
            {
                await RecordAsync(job, span, now, AuditAction.LinkingAlreadyLinked, AuditResult.Rejected, customerId);
                return null;
            }

            // Only the cellphone on the bank's record, never one from the request.
            if (!await simSwap.PassesAsync(match.Cellphone, budget.Token))
            {
                await RecordAsync(job, span, now, AuditAction.LinkingSimSwapFailed, AuditResult.Rejected, customerId);
                return null;
            }

            // The budget ends at the claim; a budget that ran out just now still stops the job here.
            budget.Token.ThrowIfCancellationRequested();
            return match;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await RecordAsync(job, span, now, AuditAction.LinkingStart, AuditResult.Failed, customerId);
            return null;
        }
    }

    /// <summary>
    /// Claims the send, sends, and releases the claim when the send fails (decided 2026-10-30). The send is bounded by
    /// SMTP's own timeout and the host's shutdown, not the budget, and is never retried. The release and the audit saves
    /// run to completion, so a claim is never left stamped by a cancelled release.
    /// </summary>
    private async Task ClaimAndSendAsync(LinkingJob job, Activity? span, CustomerMatch match, DateTimeOffset now, CancellationToken stoppingToken)
    {
        string code = Otp.NewCode();
        byte[] otpHmac = Otp.Hmac(options.Value.OtpHmacKeyBytes(), job.ChallengeId, code);
        int claimed = await db.Set<LinkingChallenge>().ClaimAsync(job.ChallengeId, match.CustomerId, otpHmac, now, CancellationToken.None);
        if (claimed != 1)
        {
            // A later start superseded the challenge before the claim: nothing is sent.
            await RecordAsync(job, span, now, AuditAction.LinkingOtpSent, AuditResult.Rejected, match.CustomerId);
            return;
        }

        // shortcut: a crash between the claim and the send loses this OTP and the person starts again; a durable send queue
        // with retries is the upgrade if lost codes become a support issue
        try
        {
            await sms.SendAsync(match.Cellphone, $"Your DisputeDesk OTP is {code}. It expires in 5 minutes. Never share it.", stoppingToken);
        }
        catch (Exception)
        {
            // Any failure to hand the message over, a cancelled send included, is a failed send. The exception is neither
            // logged nor put on the span: an SMTP error can name the recipient, which is the cellphone.
            await db.Set<LinkingChallenge>().ReleaseAsync(job.ChallengeId, now);
            span?.SetStatus(ActivityStatusCode.Error, "The OTP could not be sent.");
            await RecordAsync(job, span, now, AuditAction.LinkingOtpSent, AuditResult.Failed, match.CustomerId);
            return;
        }

        // A send already in flight when a later start superseded the challenge still completes; that OTP can never verify,
        // because verification accepts only the current challenge's OTP.
        await RecordAsync(job, span, now, AuditAction.LinkingOtpSent, AuditResult.Succeeded, match.CustomerId);
    }

    /// <summary>
    /// Records and saves the job's one audit entry. A failed save is logged with only the challenge ID and the action,
    /// and marks the span as failed; it does not stop the next job.
    /// </summary>
    private async Task RecordAsync(LinkingJob job, Activity? span, DateTimeOffset now, AuditAction action, AuditResult result, string? customerId)
    {
        AuditLog.Record(db, action, result, new Actor(ActorType.Customer, job.LoginSub), now, customerId, clientIp: job.ClientIp);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The exception is not logged: a database error can quote the row, which holds the login and the address.
            LogAuditFailed(logger, job.ChallengeId, action);
            span?.SetStatus(ActivityStatusCode.Error, "The audit entry could not be saved.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the {Action} audit entry of linking challenge {ChallengeId} failed")]
    private static partial void LogAuditFailed(ILogger logger, Guid challengeId, AuditAction action);
}
