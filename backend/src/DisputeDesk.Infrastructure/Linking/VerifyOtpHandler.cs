using System.Net;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// Verifies an OTP for the signed-in, unlinked login and links it (FR-5 to FR-7, AD-12). Only the login's current
/// challenge is used; no request field names a challenge or a customer. Every attempt is counted first, whatever the
/// challenge holds, so a blank, released or expired challenge answers and locks exactly like a wrong code. A correct code
/// consumes the challenge and inserts the link in one transaction; the session keeps its key, and the next request reads
/// the link (AD-7, AD-10). In Infrastructure, beside <see cref="LinkingJobSteps"/>, because it writes
/// <see cref="CustomerLink"/> and runs the attempt statement through <see cref="DbContext.Database"/>.
/// </summary>
public sealed partial class VerifyOtpHandler(
    AppDbContext db, TimeProvider time, IOptions<LinkingOptions> options, LinkingMetrics metrics, ILogger<VerifyOtpHandler> logger)
{
    /// <summary>Succeeds when the login is now linked; otherwise <c>otp-incorrect</c> or <c>linking-locked</c>.</summary>
    public async Task<Result> HandleAsync(string code, string loginSub, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        // The one clock reading (AD-4).
        DateTimeOffset now = time.GetUtcNow();
        var attempt = new Attempt(loginSub, clientIp, now);

        CountedAttempt? counted = await db.Database.CountAttemptAsync(loginSub, cancellationToken);
        if (counted is null)
        {
            return await AnswerUncountedAsync(attempt, code, cancellationToken);
        }

        bool matches = Otp.Matches(options.Value.OtpHmacKeyBytes(), counted.Id, code, counted.OtpHmac);
        // A blank or released challenge has no expiry; it never matches either.
        bool expired = counted.ExpiresAtUtc is DateTimeOffset expiresAt && now >= expiresAt;
        if (matches && !expired)
        {
            return await LinkAsync(attempt, counted, cancellationToken);
        }

        return await RefuseAsync(attempt, counted, expired ? AuditAction.LinkingOtpExpired : AuditAction.LinkingOtpIncorrect, lostConsume: false);
    }

    /// <summary>
    /// No attempt was counted: the login's current challenge is locked, or it has none. A login with none holds no OTP and
    /// can never succeed, so it gets a first wrong code's answer every time (decided 2026-10-30).
    /// </summary>
    private async Task<Result> AnswerUncountedAsync(Attempt attempt, string code, CancellationToken cancellationToken)
    {
        // The same HMAC work as a counted attempt (AD-12).
        _ = Otp.Matches(options.Value.OtpHmacKeyBytes(), Guid.Empty, code, otpHmac: null);

        LinkingChallenge? locked = await db.Set<LinkingChallenge>().AsNoTracking().SingleOrDefaultAsync(
            challenge => challenge.LoginSub == attempt.LoginSub
                && challenge.SupersededAtUtc == null
                && challenge.ConsumedAtUtc == null
                && challenge.Attempts >= LinkingChallenge.MaxAttempts,
            cancellationToken);
        if (locked is not null)
        {
            await RecordRejectedAsync(attempt, AuditAction.LinkingLocked, locked.CustomerId);
            return LinkingErrors.Locked;
        }

        await RecordRejectedAsync(attempt, AuditAction.LinkingOtpIncorrect, customerId: null);
        return LinkingErrors.OtpIncorrect(LinkingChallenge.MaxAttempts - 1);
    }

    /// <summary>
    /// Consumes the challenge, inserts the link and records <c>Linked</c> in one explicit transaction (EF Core,
    /// Transactions), so a link never commits without its audit entry. Another verification that consumed the challenge
    /// first, or linked this customer or login first, makes this one a counted wrong attempt (AD-15).
    /// </summary>
    private async Task<Result> LinkAsync(Attempt attempt, CountedAttempt counted, CancellationToken cancellationToken)
    {
        // A challenge whose OTP matched was claimed, and a claim always sets the customer.
        string customerId = counted.CustomerId ?? throw new InvalidOperationException("A claimed challenge has no customer.");

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        int consumed = await db.Set<LinkingChallenge>().ConsumeAsync(counted.Id, attempt.Now, cancellationToken);
        if (consumed != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return await RefuseAsync(attempt, counted, AuditAction.Linked, lostConsume: true);
        }

        db.Set<CustomerLink>().Add(new CustomerLink
        {
            Id = Guid.CreateVersion7(attempt.Now),
            LoginSub = attempt.LoginSub,
            CustomerId = customerId,
            LinkedAtUtc = attempt.Now,
        });
        AuditLog.Record(db, AuditAction.Linked, AuditResult.Succeeded, attempt.Actor, attempt.Now, customerId, clientIp: attempt.ClientIp);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (UniqueViolationException violation) when (violation.Error == LinkingErrors.LinkClash)
        {
            // The consume rolls back with the failed insert, so the challenge stays current and can lock on this attempt.
            await transaction.RollbackAsync(CancellationToken.None);
            return await RefuseAsync(attempt, counted, AuditAction.Linked, lostConsume: false);
        }

        metrics.RecordCompleted();
        return Result.Success();
    }

    /// <summary>
    /// The answer to a counted attempt that did not link: <c>otp-incorrect</c> with the attempts left, or
    /// <c>linking-locked</c> once the last one is used. The attempt that locks the challenge is audited as
    /// <c>LinkingLocked</c> and counted once in the metric. An attempt that lost the consume
    /// (<paramref name="lostConsume"/>) to a parallel verification or a later start did not lock the challenge, which is
    /// consumed or superseded, so it stays <c>Linked</c> and counts nothing.
    /// </summary>
    private async Task<Result> RefuseAsync(Attempt attempt, CountedAttempt counted, AuditAction action, bool lostConsume)
    {
        if (counted.Attempts < LinkingChallenge.MaxAttempts)
        {
            await RecordRejectedAsync(attempt, action, counted.CustomerId);
            return LinkingErrors.OtpIncorrect(LinkingChallenge.MaxAttempts - counted.Attempts);
        }

        if (lostConsume)
        {
            await RecordRejectedAsync(attempt, action, counted.CustomerId);
            return LinkingErrors.Locked;
        }

        metrics.RecordLocked();
        await RecordRejectedAsync(attempt, AuditAction.LinkingLocked, counted.CustomerId);
        return LinkingErrors.Locked;
    }

    /// <summary>
    /// Records and saves one <c>Rejected</c> entry. A failed save leaves the answer unchanged and logs only the action:
    /// a database error can quote the row, which holds the login and the address.
    /// </summary>
    private async Task RecordRejectedAsync(Attempt attempt, AuditAction action, string? customerId)
    {
        AuditLog.Record(db, action, AuditResult.Rejected, attempt.Actor, attempt.Now, customerId, clientIp: attempt.ClientIp);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            LogAuditFailed(logger, action);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the {Action} audit entry of an OTP verification failed")]
    private static partial void LogAuditFailed(ILogger logger, AuditAction action);

    /// <summary>Who is verifying, from where, and the request's one instant.</summary>
    private sealed record Attempt(string LoginSub, IPAddress? ClientIp, DateTimeOffset Now)
    {
        public Actor Actor => new(ActorType.Customer, LoginSub);

        /// <summary>Only the type name: a record's generated <c>ToString</c> would print the login and the address.</summary>
        public override string ToString() => nameof(Attempt);
    }
}
