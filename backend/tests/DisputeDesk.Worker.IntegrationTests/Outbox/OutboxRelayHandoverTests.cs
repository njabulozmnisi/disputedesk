using Confluent.Kafka;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 4 of story 4.2: a leader that loses its lock connection while a send is in flight loses no event. Its
/// delivery report arrives after the connection is gone, so it cannot mark the row Sent; the next leader sends the row
/// again with the same CloudEvent id (AD-17, Delivery guarantee: at-least-once; PostgreSQL, Advisory Locks).
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayHandoverTests(OutboxFixture outbox)
{
    private const string LeaderLine = "Outbox relay holds the outbox lock";

    [Fact]
    public async Task Relay_LockLostWithASendInFlight_NextLeaderSendsEveryVersionAndTheOldLeaderMarksNothingSent()
    {
        RelayClock leaderClock = OutboxFixture.NewClock();
        // A day ahead, so each SentAtUtc shows which relay wrote it.
        var nextClock = new RelayClock(leaderClock.GetUtcNow().AddDays(1));

        await using RelayHost leader = await outbox.StartRelayAsync(leaderClock);
        // Leader, every due row already sent, waiting out its idle loop.
        await OutboxFixture.WaitUntilAsync(() => IsLeader(leader) && leaderClock.RelayWaitsNow);
        await using RelayHost next = await outbox.StartRelayAsync(nextClock);
        await OutboxFixture.WaitUntilAsync(() => nextClock.RelayWaitsNow);
        Assert.False(IsLeader(next));

        DateTimeOffset at = leaderClock.GetUtcNow();
        OutboxMessage first = await outbox.RaiseAsync(at);
        OutboxMessage[] rows = [first, await outbox.AddVersionAsync(first.DisputeId, 2, at), await outbox.AddVersionAsync(first.DisputeId, 3, at)];

        await outbox.PauseBrokerAsync();
        try
        {
            leaderClock.Advance(TimeSpan.FromSeconds(10));
            await OutboxFixture.WaitUntilAsync(() => leader.SendStarted(first.Id));
            await outbox.TerminateLockConnectionAsync();
        }
        finally
        {
            // The old leader's delivery report now arrives after its connection is gone.
            await outbox.UnpauseBrokerAsync();
        }

        await OutboxFixture.WaitUntilAsync(() => leader.LogLines.Any(line => line.StartsWith("Outbox relay cannot reach postgres", StringComparison.Ordinal)));
        await outbox.WaitUntilSentAsync(rows.Select(row => row.Id), nextClock);

        // The old leader caught the failed update and is still running; it never retook the lock.
        Assert.False(leader.RelayTask?.IsCompleted);
        Assert.True(IsLeader(next));
        foreach (OutboxMessage row in rows)
        {
            OutboxMessage after = await outbox.RowAsync(row.Id);
            Assert.Equal(OutboxStatus.Sent, after.Status);
            Assert.True(after.SentAtUtc >= nextClock.Start, $"Version {row.DisputeVersion} was marked Sent by the old leader.");
        }

        // At least one copy of each version, and a copy carries its first copy's CloudEvent id; not strict order.
        List<ConsumeResult<string, string>> records = outbox.RecordsFor(first.DisputeId);
        Assert.Equal([1, 2, 3], (int[])[.. records.Select(OutboxFixture.DisputeVersion).Distinct().Order()]);
        Assert.All(records.GroupBy(OutboxFixture.DisputeVersion), copies => Assert.Single(copies.Select(OutboxFixture.CloudEventId).Distinct()));
    }

    private static bool IsLeader(RelayHost host) => host.LogLines.Any(line => line.StartsWith(LeaderLine, StringComparison.Ordinal));
}
