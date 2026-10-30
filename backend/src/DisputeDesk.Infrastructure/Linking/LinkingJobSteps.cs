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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// One linking job, off the request path (AD-12), in this order: the core-banking match, the link check, then one
/// transaction holding the customer's lock for the hourly send cap, the SIM-swap check on the cellphone on the bank's
/// record (failing closed) and the claim; then the send and, when the send fails, the release. Every stop records one
/// audit entry with the login, the client IP and the job's trace. Scoped: one instance, with its own database context,
/// per job.
/// </summary>
public sealed partial class LinkingJobSteps(
    AppDbContext db,
    ICoreBankingClient coreBanking,
    ISimSwapCheck simSwap,
    ISmsSender sms,
    TimeProvider time,
    IOptions<LinkingOptions> options,
    LinkingMetrics metrics,
    IServiceScopeFactory scopes,
    ILogger<LinkingJobSteps> logger)
{
    /// <summary>At most this many claimed sends per customer in any hour, delivered or not (FR-4, R6).</summary>
    private const int SendsPerHour = 3;

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
        // The one clock reading of the job (AD-4). The cap count, the claim and the release use this same instant, which
        // Npgsql stores at the same whole microseconds each time, so the release matches the claim.
        DateTimeOffset now = time.GetUtcNow();
        string code = Otp.NewCode();

        CustomerMatch? match = await CheckAndClaimAsync(job, span, code, now, stoppingToken);
        if (match is not null)
        {
            await SendAsync(job, span, match, code, now, stoppingToken);
        }
    }

    /// <summary>
    /// The checks and the claim, within the job's budget (AD-12). Returns the match once this job may send, otherwise
    /// records the stop and returns null. An unexpected exception is a stop too: it is recorded as <c>LinkingStart</c>
    /// <c>Failed</c> through a fresh scope, and the next job still runs.
    /// </summary>
    private async Task<CustomerMatch?> CheckAndClaimAsync(LinkingJob job, Activity? span, string code, DateTimeOffset now, CancellationToken stoppingToken)
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
                    await RecordAsync(db, job, span, now, AuditAction.LinkingNoMatch, AuditResult.Rejected, customerId: null);
                    return null;
                }

                await RecordAsync(db, job, span, now, AuditAction.LinkingStart, AuditResult.Failed, customerId: null);
                return null;
            }

            CustomerMatch match = matched.Value;
            customerId = match.CustomerId;
            bool isLinked = await db.Set<CustomerLink>().AnyAsync(link => link.CustomerId == match.CustomerId, budget.Token);
            if (isLinked)
            {
                await RecordAsync(db, job, span, now, AuditAction.LinkingAlreadyLinked, AuditResult.Rejected, customerId);
                return null;
            }

            return await ClaimUnderCustomerLockAsync(job, span, match, code, now, budget.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await RecordAsync(db, job, span, now, AuditAction.LinkingStart, AuditResult.Failed, customerId);
            return null;
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // The transaction, if one was open, has rolled back, which releases the customer's lock. Only the type is
            // logged: an exception's message can carry the details the job holds (OWASP Logging Cheat Sheet).
            LogUnexpectedStop(logger, job.ChallengeId, exception.GetType().Name);
            span?.SetStatus(ActivityStatusCode.Error, "The job stopped on an unexpected error.");
            // The job's own context may still track the work that failed, so the entry is saved through a fresh one.
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await RecordAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), job, span, now, AuditAction.LinkingStart, AuditResult.Failed, customerId);
            return null;
        }
    }

    /// <summary>
    /// The hourly cap, the SIM-swap check and the claim, in one transaction whose first statement takes the customer's
    /// advisory lock, so parallel jobs for one customer count and claim one at a time. It runs at the default Read
    /// Committed, so the count after the lock sees the claim the previous holder committed (EF Core, Transactions:
    /// controlling transactions). It commits before the send, so no transaction or connection is held while the SMS
    /// goes out.
    /// </summary>
    private async Task<CustomerMatch?> ClaimUnderCustomerLockAsync(
        LinkingJob job, Activity? span, CustomerMatch match, string code, DateTimeOffset now, CancellationToken budget)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(budget);
        // The lock wait runs under the budget, so a wait past it ends as the budget's LinkingStart Failed.
        await db.Database.LockCustomerAsync(match.CustomerId, budget);
        int sends = await db.Set<LinkingChallenge>().CountSendsAfterAsync(match.CustomerId, now - TimeSpan.FromHours(1), budget);
        if (sends >= SendsPerHour)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            metrics.RecordCapped();
            await RecordAsync(db, job, span, now, AuditAction.LinkingSendCapped, AuditResult.Rejected, match.CustomerId);
            return null;
        }

        // Only the cellphone on the bank's record, never one from the request.
        if (!await simSwap.PassesAsync(match.Cellphone, budget))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await RecordAsync(db, job, span, now, AuditAction.LinkingSimSwapFailed, AuditResult.Rejected, match.CustomerId);
            return null;
        }

        // The budget ends at the claim; a budget that ran out just now still stops the job here.
        budget.ThrowIfCancellationRequested();
        byte[] otpHmac = Otp.Hmac(options.Value.OtpHmacKeyBytes(), job.ChallengeId, code);
        int claimed = await db.Set<LinkingChallenge>().ClaimAsync(job.ChallengeId, match.CustomerId, otpHmac, now, CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        if (claimed != 1)
        {
            // A later start superseded the challenge, or three wrong codes locked it, before the claim: nothing is sent.
            await RecordAsync(db, job, span, now, AuditAction.LinkingOtpSent, AuditResult.Rejected, match.CustomerId);
            return null;
        }

        return match;
    }

    /// <summary>
    /// Sends the claimed OTP, and releases the claim when the send fails (decided 2026-10-30). The send is bounded by
    /// SMTP's own timeout and the host's shutdown, not the budget, and is never retried. The release and the audit saves
    /// run to completion, so a claim is never left with a live OTP by a cancelled release.
    /// </summary>
    private async Task SendAsync(LinkingJob job, Activity? span, CustomerMatch match, string code, DateTimeOffset now, CancellationToken stoppingToken)
    {
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
            await RecordAsync(db, job, span, now, AuditAction.LinkingOtpSent, AuditResult.Failed, match.CustomerId);
            return;
        }

        // A send already in flight when a later start superseded the challenge still completes; that OTP can never verify,
        // because verification accepts only the current challenge's OTP.
        await RecordAsync(db, job, span, now, AuditAction.LinkingOtpSent, AuditResult.Succeeded, match.CustomerId);
    }

    /// <summary>
    /// Records the job's one audit entry and saves it through <paramref name="context"/>. A failed save is logged with only
    /// the challenge ID and the action, and marks the span as failed; it does not stop the next job.
    /// </summary>
    private async Task RecordAsync(
        AppDbContext context, LinkingJob job, Activity? span, DateTimeOffset now, AuditAction action, AuditResult result, string? customerId)
    {
        AuditLog.Record(context, action, result, new Actor(ActorType.Customer, job.LoginSub), now, customerId, clientIp: job.ClientIp);
        try
        {
            await context.SaveChangesAsync(CancellationToken.None);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Linking challenge {ChallengeId} stopped on an unexpected {ErrorType}")]
    private static partial void LogUnexpectedStop(ILogger logger, Guid challengeId, string errorType);
}
