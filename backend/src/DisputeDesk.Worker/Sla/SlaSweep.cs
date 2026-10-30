using System.Diagnostics;
using System.Diagnostics.Metrics;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DisputeDesk.Worker.Sla;

/// <summary>
/// The SLA sweep (AD-18, FR-26). Every 30 seconds it escalates the open disputes whose SLA is due, then records one
/// <see cref="SlaWarningRaised"/> on each open dispute due within 48 hours. Each batch is one transaction: it claims up to
/// 50 rows with <c>FOR UPDATE SKIP LOCKED</c>, then, inside one savepoint per dispute, replays it, lets the aggregate
/// decide and saves it through the commit (AD-14), so one failed dispute does not undo the others (EF Core,
/// Transactions: savepoints). Batches repeat until one comes back short, which catches up after downtime.
/// Two sweeps exclude each other through the row locks alone (NFR-ARC-4).
/// </summary>
public sealed partial class SlaSweep(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IMeterFactory meterFactory,
    ILogger<SlaSweep> logger) : BackgroundService
{
    /// <summary>The span of one batch, from claim to commit; its context is the traceparent of the batch's events.</summary>
    public const string BatchSpanName = "disputedesk.sla_sweep.batch";

    /// <summary>The sweep's actor (AD-14a): the one automation actor any code path records, besides the seeder.</summary>
    public static readonly Actor SweepActor = new(ActorType.Automation, Actor.SlaSweepId);

    /// <summary>
    /// Touched every loop and after every batch; the Compose health check fails when it is older than 90 seconds, three
    /// missed loops (AD-25). The system temporary directory is <c>/tmp/</c> in the container.
    /// </summary>
    public static readonly string HeartbeatPath = Path.Combine(Path.GetTempPath(), "heartbeat-sweep");

    /// <summary>How often the sweep runs (AD-18: at least once a minute).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private const string DisputeSavepoint = "sla_sweep_dispute";

    private static readonly ActivitySource Source = new(Telemetry.ActivitySourceName);

    private readonly Counter<long> _escalations = meterFactory.Create(Telemetry.MeterName)
        .CreateCounter<long>("disputedesk.sla.escalations", description: "Disputes the SLA sweep escalated.");

    private readonly Counter<long> _actions = meterFactory.Create(Telemetry.MeterName)
        .CreateCounter<long>("disputedesk.dispute_actions", description: "Dispute actions that succeeded, by action and actor type.");

    private enum Claim
    {
        Overdue,
        Warning,
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Each batch span is a root span, never a child of whatever started the host.
        Activity.Current = null;
        using var timer = new PeriodicTimer(Interval, timeProvider);
        try
        {
            do
            {
                TouchHeartbeat();
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The next loop tries again. Only the type is logged: a message can carry data (OWASP Logging Cheat Sheet).
                    LogSweepFailed(exception.GetType().Name);
                }

                TouchHeartbeat();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; an unfinished batch rolls back and the next start takes it again.
        }
    }

    /// <summary>
    /// One loop: escalation batches until one comes back short, then warning batches the same way. A full batch that
    /// changed nothing also ends the run, or a batch of disputes that all fail would be claimed again and again.
    /// </summary>
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        foreach (Claim claim in new[] { Claim.Overdue, Claim.Warning })
        {
            int claimed;
            int changed;
            do
            {
                (claimed, changed) = await RunBatchAsync(claim, cancellationToken);
            }
            while (claimed == SlaQueries.BatchSize && changed > 0);
        }
    }

    /// <summary>
    /// One batch in one transaction, holding its row locks from claim to commit. A short batch means only that no more rows
    /// were claimable then: rows another transaction holds are taken by a later loop. Returns how many rows it claimed and
    /// how many of those it changed.
    /// </summary>
    private async Task<(int Claimed, int Changed)> RunBatchAsync(Claim claim, CancellationToken cancellationToken)
    {
        // The batch's one clock reading (AD-4), cut to the whole microseconds a Postgres timestamptz keeps.
        DateTimeOffset now = ToWholeMicroseconds(timeProvider.GetUtcNow());
        using Activity? span = Source.StartActivity(BatchSpanName);
        span?.SetTag("disputedesk.sla_sweep.claim", claim == Claim.Overdue ? "overdue" : "warning");

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Each dispute has its own savepoint around its load and its save (TryChangeAsync), so EF Core's own, around the save only, is not needed.
        db.Database.AutoSavepointsEnabled = false;
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        List<Guid> claimed = claim == Claim.Overdue
            ? await db.ClaimOverdueAsync(now, cancellationToken)
            : await db.ClaimWarningsAsync(now, Dispute.SlaWarningLeadTime, cancellationToken);

        int changed = 0;
        foreach (Guid disputeId in claimed)
        {
            if (await TryChangeAsync(db, transaction, disputeId, claim, now, cancellationToken))
            {
                changed++;
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Counted only once committed, so a dispute whose save rolled back counts nothing (AD-20).
        if (claim == Claim.Overdue && changed > 0)
        {
            _escalations.Add(changed);
            _actions.Add(changed, new KeyValuePair<string, object?>("action", "escalate"), new KeyValuePair<string, object?>("actor_type", "automation"));
        }

        span?.SetTag("disputedesk.sla_sweep.claimed", claimed.Count);
        span?.SetTag("disputedesk.sla_sweep.changed", changed);
        if (claimed.Count > 0)
        {
            LogBatch(claim, changed, claimed.Count);
        }

        TouchHeartbeat();
        return (claimed.Count, changed);
    }

    /// <summary>
    /// Changes one claimed dispute inside its own savepoint, around both the stream load and the save, so any failure,
    /// in either, is undone alone and leaves the batch's transaction usable (EF Core, Transactions: savepoints). Without
    /// it a failed read would abort the PostgreSQL transaction, and Npgsql's commit of an aborted transaction rolls the
    /// whole batch back without throwing. A version clash (AD-15) rolls back and decides once more, so a dispute another
    /// action escalated or closed first is skipped; any other failure is skipped until the next loop. True when saved.
    /// </summary>
    private async Task<bool> TryChangeAsync(
        AppDbContext db, IDbContextTransaction transaction, Guid disputeId, Claim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            await transaction.CreateSavepointAsync(DisputeSavepoint, cancellationToken);
            try
            {
                bool changed = await ChangeAsync(db, disputeId, claim, now, cancellationToken);
                await transaction.ReleaseSavepointAsync(DisputeSavepoint, cancellationToken);
                return changed;
            }
            catch (UniqueViolationException clash) when (clash.Error == DisputeErrors.VersionClash)
            {
                await transaction.RollbackToSavepointAsync(DisputeSavepoint, cancellationToken);
                await transaction.ReleaseSavepointAsync(DisputeSavepoint, cancellationToken);
                LogVersionClash(disputeId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await transaction.RollbackToSavepointAsync(DisputeSavepoint, cancellationToken);
                await transaction.ReleaseSavepointAsync(DisputeSavepoint, cancellationToken);
                LogDisputeFailed(disputeId, exception.GetType().Name);
                return false;
            }
        }

        return false;
    }

    /// <summary>Replays the dispute and lets the aggregate decide; saves and returns true when it changed (AD-14).</summary>
    private static async Task<bool> ChangeAsync(AppDbContext db, Guid disputeId, Claim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<StoredEvent> stream = await LoadStreamAsync(db, disputeId, cancellationToken);
        var dispute = Dispute.Replay(stream);
        // Time never runs backwards within a stream (AD-15).
        DateTimeOffset instant = Later(now, stream[^1].OccurredAtUtc);
        Result decided = claim == Claim.Overdue ? EscalateIfOverdue(dispute, instant) : dispute.RecordSlaWarning(SweepActor, instant);
        if (decided.IsFailure)
        {
            return false;
        }

        // The replayed dispute is not tracked; Update writes the read model with its stored event (AD-14).
        db.Set<Dispute>().Update(dispute);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Escalates through <see cref="Dispute.Escalate"/> only while the SLA is still due (FR-26); the status is the aggregate's check.</summary>
    private static Result EscalateIfOverdue(Dispute dispute, DateTimeOffset now) =>
        dispute.SlaDueAtUtc <= now ? dispute.Escalate(SweepActor, now) : DisputeErrors.ActionNotAllowed;

    /// <summary>
    /// The dispute's stream, through the named ownership bypass: the sweep acts for no customer (AD-11). Only the named
    /// filter is ignored (EF Core, Global query filters: named filters).
    /// </summary>
    private static async Task<List<StoredEvent>> LoadStreamAsync(AppDbContext db, Guid disputeId, CancellationToken cancellationToken)
    {
        List<StoredEventRow> rows = await db.Set<StoredEventRow>().IgnoreQueryFilters([AppDbContext.OwnershipFilter]).AsNoTracking()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => row.ToStoredEvent())];
    }

    private void TouchHeartbeat()
    {
        if (!File.Exists(HeartbeatPath))
        {
            File.WriteAllBytes(HeartbeatPath, []);
        }

        File.SetLastWriteTimeUtc(HeartbeatPath, timeProvider.GetUtcNow().UtcDateTime);
    }

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    private static DateTimeOffset ToWholeMicroseconds(DateTimeOffset instant) =>
        instant.AddTicks(-(instant.Ticks % TimeSpan.TicksPerMicrosecond));

    // Counts and dispute IDs only, never a customer, an account, a transaction or an exception's message (AD-20).
    [LoggerMessage(Level = LogLevel.Information, Message = "SLA sweep {Claim} batch changed {Changed} of {Claimed} claimed disputes")]
    private partial void LogBatch(Claim claim, int changed, int claimed);

    [LoggerMessage(Level = LogLevel.Information, Message = "SLA sweep lost a version race on dispute {DisputeId} and decides again")]
    private partial void LogVersionClash(Guid disputeId);

    [LoggerMessage(Level = LogLevel.Error, Message = "SLA sweep could not change dispute {DisputeId} ({ErrorType}); the next loop tries again")]
    private partial void LogDisputeFailed(Guid disputeId, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "SLA sweep loop failed ({ErrorType}); the next loop tries again")]
    private partial void LogSweepFailed(string errorType);
}
