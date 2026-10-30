using System.Diagnostics;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Worker.IntegrationTests.Outbox;
using DisputeDesk.Worker.Sla;
using Npgsql;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// AC2 and AC3 (R22, R26, R27; FR-26, NFR-ARC-4, NFR-RES-1): batches of at most 50, each one transaction from claim to
/// commit with a savepoint per dispute; <c>SKIP LOCKED</c> against a held lock; two sweeps at once; catch-up after
/// downtime with the heartbeat touched after each batch.
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaBatchTests(SlaFixture fixture)
{
    private static readonly DateTimeOffset LongOverdue = SweepRig.RaisedAt + Dispute.Sla + TimeSpan.FromDays(2);

    [Fact]
    public async Task Sweep_120Overdue_Runs3BatchesOf50_50And20_EachOneTransactionWithASavepointPerDispute()
    {
        var transactions = new TransactionCounter();
        await using SweepRig rig = await SweepRig.CreateAsync(fixture, transactions);
        List<Guid> disputeIds = await rig.RaiseManyAsync(120, SweepRig.RaisedAt);
        rig.Clock.SetUtcNow(LongOverdue);

        await rig.SweepAsync();

        IReadOnlyList<Activity> overdue = rig.BatchSpans("overdue");
        Assert.Equal([50, 50, 20], overdue.Select(span => (int?)span.GetTagItem("disputedesk.sla_sweep.claimed")));
        Assert.Equal([50, 50, 20], overdue.Select(span => (int?)span.GetTagItem("disputedesk.sla_sweep.changed")));
        // One transaction per batch, the warning batch included, and one savepoint per saved dispute.
        Assert.Equal(overdue.Count + rig.BatchSpans("warning").Count, transactions.Committed);
        Assert.Equal(120, transactions.Savepoints);
        Assert.All(await rig.EscalationCountsAsync(disputeIds), counts => Assert.Equal((1, 1), counts.Value));
    }

    [Theory]
    [InlineData(49, new[] { 49 })]
    [InlineData(50, new[] { 50, 0 })] // a full batch is followed by one more claim, which comes back empty
    [InlineData(51, new[] { 50, 1 })]
    public async Task Sweep_EachSideOfTheBatchSize_ClaimsAtMost50PerBatchAndRepeatsOnlyAfterAFullOne(int overdue, int[] expected)
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        List<Guid> disputeIds = await rig.RaiseManyAsync(overdue, SweepRig.RaisedAt);
        rig.Clock.SetUtcNow(LongOverdue);

        await rig.SweepAsync();

        Assert.Equal(expected, rig.BatchSpans("overdue").Select(span => (int?)span.GetTagItem("disputedesk.sla_sweep.claimed") ?? -1));
        Assert.All(await rig.EscalationCountsAsync(disputeIds), counts => Assert.Equal((1, 1), counts.Value));
    }

    [Fact]
    public async Task Sweep_OneDisputeFailsToSave_RollsBackToItsSavepointAndTheRestOfTheBatchCommits()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        List<Guid> disputeIds = await rig.RaiseManyAsync(3, SweepRig.RaisedAt);
        Guid failing = disputeIds[1];
        await FailEscalationInsertAsync(rig, failing);
        rig.Clock.SetUtcNow(LongOverdue);

        await rig.SweepAsync();

        // The Disputes update ran before the refused insert in the same save, and the savepoint undid it.
        Dispute row = await rig.DisputeRowAsync(failing);
        Assert.Equal(DisputeStatus.Submitted, row.Status);
        Assert.Equal(1, row.Version);
        Assert.Single(await rig.StreamAsync(failing));
        Assert.Single(await rig.OutboxAsync(failing));
        Dictionary<Guid, (int, int)> counts = await rig.EscalationCountsAsync(disputeIds);
        Assert.Equal((1, 1), counts[disputeIds[0]]);
        Assert.Equal((1, 1), counts[disputeIds[2]]);
        // AC5: only the two committed escalations are counted.
        Assert.Equal(2, rig.Total("disputedesk.sla.escalations"));
        Assert.Equal(2, rig.Total("disputedesk.dispute_actions", ("action", "escalate"), ("actor_type", "automation")));
        // AC8: the failure is logged with the dispute ID and the exception's type only, never its message.
        Assert.Contains(rig.SweepLogLines, line => line.Contains(failing.ToString(), StringComparison.Ordinal) && line.Contains("DbUpdateException", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.SweepLogLines, line => line.Contains("FAILMARK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sweep_ASaveFailedOnAnEarlierLoop_EscalatesThatDisputeOnALaterLoopOnceTheCauseIsGone()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        List<Guid> disputeIds = await rig.RaiseManyAsync(3, SweepRig.RaisedAt);
        Guid failing = disputeIds[1];
        await FailEscalationInsertAsync(rig, failing);
        rig.Clock.SetUtcNow(LongOverdue);
        await rig.SweepAsync();
        Assert.Equal((0, 0), (await rig.EscalationCountsAsync([failing]))[failing]);

        await DropFailEscalationAsync(rig);
        rig.Clock.Advance(SlaSweep.Interval);
        await rig.SweepAsync();

        Assert.All(await rig.EscalationCountsAsync(disputeIds), counts => Assert.Equal((1, 1), counts.Value));
        Assert.Equal(DisputeStatus.Escalated, (await rig.DisputeRowAsync(failing)).Status);
        Assert.Equal(3, rig.Total("disputedesk.sla.escalations"));
    }

    [Fact]
    public async Task Sweep_TheServerRefusesOneStreamLoad_RollsBackThatDisputeOnlyAndCountsOnlyWhatCommitted()
    {
        // A refused read aborts the PostgreSQL transaction; without a savepoint around the load, the batch's commit
        // would end in a rollback of the disputes saved before it, and the counters would still count them.
        var failLoad = new FailStreamLoad();
        await using SweepRig rig = await SweepRig.CreateAsync(fixture, failLoad);
        List<Guid> disputeIds = await rig.RaiseManyAsync(3, SweepRig.RaisedAt);
        Guid failing = disputeIds[1];
        failLoad.DisputeId = failing;
        rig.Clock.SetUtcNow(LongOverdue);

        await rig.SweepAsync();

        Dictionary<Guid, (int, int)> counts = await rig.EscalationCountsAsync(disputeIds);
        Assert.Equal((1, 1), counts[disputeIds[0]]);
        Assert.Equal((0, 0), counts[failing]);
        Assert.Equal((1, 1), counts[disputeIds[2]]);
        Assert.Equal(2, rig.Total("disputedesk.sla.escalations"));
        Assert.Equal(2, rig.Total("disputedesk.dispute_actions", ("action", "escalate"), ("actor_type", "automation")));
        Assert.Equal([2], rig.BatchSpans("overdue").Select(span => (int?)span.GetTagItem("disputedesk.sla_sweep.changed")));
        Assert.Contains(rig.SweepLogLines, line => line.Contains(failing.ToString(), StringComparison.Ordinal) && line.Contains("PostgresException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NpgsqlCommit_OfATransactionTheServerAborted_DoesNotThrowAndRollsBack()
    {
        // Pins the installed Npgsql's behaviour, which the sweep's savepoint around each dispute's load and save relies on:
        // PostgreSQL answers COMMIT in an aborted transaction with ROLLBACK, and Npgsql reports no error.
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt);
        await using var connection = new NpgsqlConnection(rig.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var update = new NpgsqlCommand("""UPDATE "Disputes" SET "Version" = 99 WHERE "Id" = @id""", connection, transaction))
        {
            update.Parameters.AddWithValue("id", disputeId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using (var refused = new NpgsqlCommand("SELECT 1 / 0", connection, transaction))
        {
            await Assert.ThrowsAsync<PostgresException>(() => refused.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, (await rig.DisputeRowAsync(disputeId)).Version);
    }

    [Fact]
    public async Task Sweep_FiveDueRowsLockedByAnotherTransaction_EscalatesTheOthersWithoutWaitingAndTheFiveOnTheNextLoop()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        List<Guid> disputeIds = await rig.RaiseManyAsync(12, SweepRig.RaisedAt);
        Guid[] held = [.. disputeIds.Take(5)];
        rig.Clock.SetUtcNow(LongOverdue);

        await using (var holder = new NpgsqlConnection(rig.ConnectionString))
        {
            await holder.OpenAsync(TestContext.Current.CancellationToken);
            await using NpgsqlTransaction holding = await holder.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await using (var lockRows = new NpgsqlCommand("""SELECT "Id" FROM "Disputes" WHERE "Id" = ANY(@ids) FOR UPDATE""", holder, holding))
            {
                lockRows.Parameters.AddWithValue("ids", held);
                await lockRows.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            // Without SKIP LOCKED the claim would wait on the held rows until this transaction ends.
            await rig.SweepAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Dictionary<Guid, (int, int)> whileHeld = await rig.EscalationCountsAsync(disputeIds);
            Assert.All(held, id => Assert.Equal((0, 0), whileHeld[id]));
            Assert.All(disputeIds.Skip(5), id => Assert.Equal((1, 1), whileHeld[id]));
            await holding.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await rig.SweepAsync();

        Assert.All(await rig.EscalationCountsAsync(disputeIds), counts => Assert.Equal((1, 1), counts.Value));
    }

    [Fact]
    public async Task Sweep_TwoAtOnceWithOverlappingTransactions_EscalateEachDisputeExactlyOnce()
    {
        // Each sweep's first claim waits, holding its locks, until the other has claimed too.
        var rendezvous = new ClaimRendezvous(parties: 2);
        await using SweepRig first = await SweepRig.CreateAsync(fixture, rendezvous.Hook);
        await using var second = SweepRig.OnSameDatabase(first, rendezvous.Hook);
        List<Guid> disputeIds = await first.RaiseManyAsync(120, SweepRig.RaisedAt);
        first.Clock.SetUtcNow(LongOverdue);
        second.Clock.SetUtcNow(LongOverdue);

        await Task.WhenAll(first.SweepAsync(), second.SweepAsync());

        Assert.All(await first.EscalationCountsAsync(disputeIds), counts => Assert.Equal((1, 1), counts.Value));
        // Both listeners see both sweeps' spans: the claims of the two sweeps add up to 120, so no row was claimed twice.
        IReadOnlyList<Activity> overdue = first.BatchSpans("overdue");
        Assert.Equal(120, overdue.Sum(span => (int?)span.GetTagItem("disputedesk.sla_sweep.claimed")));
        Assert.Equal([50, 50], overdue.Take(2).Select(span => (int?)span.GetTagItem("disputedesk.sla_sweep.claimed")));
    }

    [Fact]
    public async Task Start_AfterDowntimePast120DueTimes_RepeatsBatchesUntilShortAndTouchesTheHeartbeatAfterEachBatch()
    {
        // Each claim records the clock and the heartbeat's time, then moves the clock on 20 seconds, as a slow batch would.
        var atEachClaim = new List<(DateTime Clock, DateTime Heartbeat)>();
        SweepRig? rigRef = null;
        var hook = new ClaimHook
        {
            BeforeClaim = () =>
            {
                RecordHeartbeatAndAdvance(rigRef, atEachClaim);
                return Task.CompletedTask;
            },
        };
        await using SweepRig rig = await SweepRig.CreateAsync(fixture, hook);
        rigRef = rig;
        List<Guid> disputeIds = await rig.RaiseManyAsync(120, SweepRig.RaisedAt);
        rig.Clock.SetUtcNow(LongOverdue);
        SlaSweep sweep = rig.Sweep;

        await sweep.StartAsync(TestContext.Current.CancellationToken);
        await OutboxFixture.WaitUntilAsync(async () =>
            (await rig.EscalationCountsAsync(disputeIds)).Values.All(counts => counts == (1, 1)));
        await sweep.StopAsync(TestContext.Current.CancellationToken);

        Assert.All(await rig.EscalationCountsAsync(disputeIds), counts => Assert.Equal((1, 1), counts.Value));
        Assert.True(atEachClaim.Count >= 4, $"Only {atEachClaim.Count} claims ran.");
        // Touched since the clock last moved, at every claim: after each batch, not only once per loop.
        lock (atEachClaim)
        {
            Assert.All(atEachClaim, claim => Assert.Equal(claim.Clock, claim.Heartbeat));
        }
    }

    private static void RecordHeartbeatAndAdvance(SweepRig? rig, List<(DateTime Clock, DateTime Heartbeat)> atEachClaim)
    {
        if (rig is null)
        {
            return;
        }

        lock (atEachClaim)
        {
            atEachClaim.Add((rig.Clock.GetUtcNow().UtcDateTime, File.GetLastWriteTimeUtc(SlaSweep.HeartbeatPath)));
        }

        rig.Clock.Advance(TimeSpan.FromSeconds(20));
    }

    /// <summary>A trigger refuses the <c>DisputeEscalated</c> insert of one dispute, as a failing save would be refused.</summary>
    private static async Task FailEscalationInsertAsync(SweepRig rig, Guid disputeId)
    {
        await using var connection = new NpgsqlConnection(rig.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            $$"""
            CREATE FUNCTION fail_escalation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW."DisputeId" = '{{disputeId}}' AND NEW."EventType" = '{{nameof(DisputeEscalated)}}' THEN
                RAISE EXCEPTION 'test failure FAILMARK';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER fail_escalation BEFORE INSERT ON "StoredEvents" FOR EACH ROW EXECUTE FUNCTION fail_escalation();
            """,
            connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task DropFailEscalationAsync(SweepRig rig)
    {
        await using var connection = new NpgsqlConnection(rig.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""DROP TRIGGER fail_escalation ON "StoredEvents"; DROP FUNCTION fail_escalation();""", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
