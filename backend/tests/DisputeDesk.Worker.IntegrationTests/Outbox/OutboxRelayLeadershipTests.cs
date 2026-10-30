using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>Criterion 3: only the holder of the advisory lock produces, on its own lock connection (AD-17, Leadership).</summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayLeadershipTests(OutboxFixture outbox)
{
    private const string LeaderLine = "Outbox relay holds the outbox lock";

    [Fact]
    public async Task TwoHosts_SamePendingRows_OnlyTheLockHolderPublishesEachRowOnce()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        // Due one minute ahead, so both hosts are already looping when the rows become due together.
        DateTimeOffset due = clock.GetUtcNow().AddMinutes(1);
        OutboxMessage[] rows = [.. await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => outbox.RaiseAsync(due)))];

        await using RelayHost first = await outbox.StartRelayAsync(clock);
        await using RelayHost second = await outbox.StartRelayAsync(clock);
        await OutboxFixture.WaitUntilAsync(() => LeaderCount(first) + LeaderCount(second) == 1);
        clock.Advance(TimeSpan.FromMinutes(1));
        await outbox.WaitUntilSentAsync(rows.Select(row => row.Id), clock);
        // Three more 10-second loops each, in which the other host keeps trying for the lock.
        for (int step = 0; step < 3; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, LeaderCount(first) + LeaderCount(second));
        Assert.All(rows, row =>
        {
            Assert.Single(outbox.RecordsFor(row.DisputeId));
            // Both exporters see every DisputeDesk span in the process (ActivitySource listeners are process-wide).
            Assert.Single(first.ProducerSpansFor(row.Id));
        });
    }

    [Fact]
    public async Task LockConnection_Terminated_RelayStopsProducingThenRetakesTheLockAndPublishes()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        await using RelayHost host = await outbox.StartRelayAsync(clock);
        await OutboxFixture.WaitUntilAsync(() => LeaderCount(host) == 1);

        await TerminateLockConnectionAsync();
        OutboxMessage row = await outbox.RaiseAsync(clock.GetUtcNow());
        await outbox.WaitUntilSentAsync([row.Id], clock);

        Assert.Equal(2, LeaderCount(host));
        Assert.Contains(host.LogLines, line => line.StartsWith("Outbox relay cannot reach postgres", StringComparison.Ordinal));
        Assert.Single(outbox.RecordsFor(row.DisputeId));
    }

    [Fact]
    public async Task Relay_LockHeldElsewhere_ProducesNothing()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        OutboxMessage row = await outbox.RaiseAsync(clock.GetUtcNow());
        await using var holder = new NpgsqlConnection(outbox.LockHolderConnectionString);
        await holder.OpenAsync(TestContext.Current.CancellationToken);
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", holder))
        {
            take.Parameters.Add(new NpgsqlParameter<long>("key", OutboxRelay.LockKey));
            await take.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using (RelayHost host = await outbox.StartRelayAsync(clock))
        {
            for (int step = 0; step < 3; step++)
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                await Task.Delay(200, TestContext.Current.CancellationToken);
            }

            Assert.Equal(0, LeaderCount(host));
        }

        Assert.Equal(OutboxStatus.Pending, (await outbox.RowAsync(row.Id)).Status);
        Assert.Empty(outbox.RecordsFor(row.DisputeId));
    }

    private static int LeaderCount(RelayHost host) => host.LogLines.Count(line => line.StartsWith(LeaderLine, StringComparison.Ordinal));

    /// <summary>Ends the session that holds the relay's advisory lock, as a dropped connection would (PostgreSQL, pg_terminate_backend).</summary>
    private async Task TerminateLockConnectionAsync()
    {
        await using var admin = new NpgsqlConnection(outbox.ConnectionString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND objid::bigint = @key", admin);
        terminate.Parameters.Add(new NpgsqlParameter<long>("key", OutboxRelay.LockKey));
        Assert.True(await terminate.ExecuteScalarAsync(TestContext.Current.CancellationToken) is true);
    }
}
