using System.Data.Common;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Application.Operations.Actions;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Worker.IntegrationTests.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// AC4 (R22, R26; FR-26, AD-15): a sweep and an operations escalate (story 5.2) on the same dispute end with exactly one
/// <c>DisputeEscalated</c>, with no deadlock and no unexpected failure on either side; a sweep that loses a version race
/// rolls back to its savepoint, replays and decides again.
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaRaceTests(SlaFixture fixture)
{
    private static readonly DateTimeOffset Overdue = SweepRig.RaisedAt + Dispute.Sla + TimeSpan.FromHours(1);
    private static readonly Actor Operations = new(ActorType.Operations, "ops-race");

    [Fact]
    public async Task OperationsEscalate_WhileTheSweepHoldsItsClaim_WaitsThenGets409AndTheSweepsEscalationIsTheOnlyOne()
    {
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new ClaimHook
        {
            AfterClaim = async () =>
            {
                claimed.TrySetResult();
                await release.Task;
            },
        };
        await using SweepRig rig = await SweepRig.CreateAsync(fixture, hook);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt, DisputeStatus.UnderReview);
        rig.Clock.SetUtcNow(Overdue);

        Task sweep = rig.SweepAsync();
        await claimed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Task<Result<OperationsActionResult>> operations = EscalateAsOperationsAsync(rig, disputeId);
        // The operations save is waiting on the sweep's row lock before the sweep goes on.
        await WaitUntilASessionWaitsOnALockAsync(rig);
        release.SetResult();
        await sweep;
        Result<OperationsActionResult> result = await operations;

        Assert.True(result.IsFailure);
        Assert.Equal("action-not-allowed", result.Error.Code);
        StoredEventRow escalated = Assert.Single(await rig.StreamAsync(disputeId), row => row.EventType == nameof(DisputeEscalated));
        Assert.Equal("worker:sla-sweep", escalated.ActorId);
        Assert.Equal((1, 1), (await rig.EscalationCountsAsync([disputeId]))[disputeId]);
    }

    [Fact]
    public async Task Sweep_WhileAnOperationsEscalateHoldsTheRowUncommitted_SkipsItAndTheOperationsEscalationIsTheOnlyOne()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt);
        rig.Clock.SetUtcNow(Overdue);
        var committing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdCommit = new HoldCommit(committing, release.Task);

        Task<Result<OperationsActionResult>> operations = EscalateAsOperationsAsync(rig, disputeId, holdCommit);
        await committing.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        // The operations save has written its rows and holds the row lock; the sweep skips the row and does not wait.
        await rig.SweepAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        release.SetResult();
        Result<OperationsActionResult> result = await operations;
        await rig.SweepAsync();

        Assert.True(result.IsSuccess);
        StoredEventRow escalated = Assert.Single(await rig.StreamAsync(disputeId), row => row.EventType == nameof(DisputeEscalated));
        Assert.Equal("ops-race", escalated.ActorId);
        Assert.Equal((1, 1), (await rig.EscalationCountsAsync([disputeId]))[disputeId]);
        Assert.Equal(0, rig.Total("disputedesk.sla.escalations"));
    }

    [Fact]
    public async Task Sweep_LosesAVersionRaceToAnEscalation_RollsBackToItsSavepointReplaysAndSkips()
    {
        var clash = new VersionClashInterceptor();
        await using SweepRig rig = await SweepRig.CreateAsync(fixture, clash);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt);
        clash.Arm(
            new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated)
            {
                DisputeId = disputeId,
                Version = 2,
                OccurredAtUtc = Overdue,
                ActorType = ActorType.Operations,
                ActorId = "ops-race",
            },
            DisputeStatus.Escalated);
        rig.Clock.SetUtcNow(Overdue);

        await rig.SweepAsync();

        Assert.True(clash.Fired);
        Assert.Contains(rig.SweepLogLines, line => line.Contains("lost a version race", StringComparison.Ordinal));
        List<StoredEventRow> stream = await rig.StreamAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(DisputeEscalated)], stream.Select(row => row.EventType));
        Assert.Equal("ops-race", stream[1].ActorId);
        // The sweep's own outbox row was rolled back with its save: only the raise's is left.
        Assert.Single(await rig.OutboxAsync(disputeId));
        Assert.Equal(DisputeStatus.Escalated, (await rig.DisputeRowAsync(disputeId)).Status);
        Assert.Equal(0, rig.Total("disputedesk.sla.escalations"));
    }

    [Fact]
    public async Task Sweep_LosesAVersionRaceToAStartReview_ReplaysAndEscalatesTheStillOverdueDisputeAtTheNextVersion()
    {
        var clash = new VersionClashInterceptor();
        await using SweepRig rig = await SweepRig.CreateAsync(fixture, clash);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt);
        clash.Arm(
            new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview)
            {
                DisputeId = disputeId,
                Version = 2,
                OccurredAtUtc = Overdue,
                ActorType = ActorType.Operations,
                ActorId = "ops-race",
            },
            DisputeStatus.UnderReview);
        rig.Clock.SetUtcNow(Overdue);

        await rig.SweepAsync();

        Assert.True(clash.Fired);
        List<StoredEventRow> stream = await rig.StreamAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeEscalated)], stream.Select(row => row.EventType));
        DisputeEscalated escalated = Assert.IsType<DisputeEscalated>(stream[2].ToStoredEvent());
        Assert.Equal(DisputeStatus.UnderReview, escalated.FromStatus);
        Assert.Equal("worker:sla-sweep", escalated.ActorId);
        Dispute row = await rig.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Escalated, row.Status);
        Assert.Equal(3, row.Version);
        Assert.Equal((1, 1), (await rig.EscalationCountsAsync([disputeId]))[disputeId]);
        Assert.Equal(1, rig.Total("disputedesk.sla.escalations"));
    }

    [Fact]
    public async Task Save_OfAnEscalation_SendsTheDisputesUpdateBeforeTheStoredEventInsert()
    {
        // The race above relies on it: a save that inserted its stored event first would hold that version's index entry
        // while waiting on the row lock, and the sweep's own insert would then wait on it: a deadlock (AD-15).
        var commands = new CommandLog();
        await using SweepRig rig = await SweepRig.CreateAsync(fixture, commands);
        await rig.RaiseAsync(SweepRig.RaisedAt);
        rig.Clock.SetUtcNow(Overdue);

        await rig.SweepAsync();

        string save = Assert.Single(commands.Commands, command => command.Contains("INSERT INTO \"StoredEvents\"", StringComparison.Ordinal));
        int update = save.IndexOf("UPDATE \"Disputes\"", StringComparison.Ordinal);
        Assert.InRange(update, 0, save.IndexOf("INSERT INTO \"StoredEvents\"", StringComparison.Ordinal));
        // The claims are sent as written, with every value a parameter (EF Core, SQL queries: SqlQuery is not composed).
        Assert.Equal(
            """
            SELECT "Id" AS "Value"
            FROM "Disputes"
            WHERE "Status" IN ('Submitted', 'UnderReview') AND "SlaDueAtUtc" <= @p0
            ORDER BY "SlaDueAtUtc"
            LIMIT @p1
            FOR UPDATE SKIP LOCKED
            """,
            commands.Commands[0].TrimEnd());
    }

    /// <summary>The operations escalate of story 5.2, run as the Api runs it, on a context of its own.</summary>
    private static async Task<Result<OperationsActionResult>> EscalateAsOperationsAsync(SweepRig rig, Guid disputeId, params IInterceptor[] interceptors)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(rig.ConnectionString).AddInterceptors(interceptors).Options);
        var actor = new CurrentOperationsActor();
        actor.Set(Operations);
        var handler = new OperationsActionHandler(db, actor, TimeProvider.System);
        return await handler.HandleAsync(disputeId, DisputeAction.Escalate, resolve: null, CancellationToken.None);
    }

    private static async Task WaitUntilASessionWaitsOnALockAsync(SweepRig rig)
    {
        await using var connection = new NpgsqlConnection(rig.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await OutboxFixture.WaitUntilAsync(async () =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", connection);
            return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0L) > 0;
        });
    }

    /// <summary>Holds a save's transaction just before it commits, after its rows are written.</summary>
    private sealed class HoldCommit(TaskCompletionSource committing, Task release) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            committing.TrySetResult();
            await release;
            return result;
        }
    }
}
