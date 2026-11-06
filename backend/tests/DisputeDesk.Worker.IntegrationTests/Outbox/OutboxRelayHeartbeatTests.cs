using System.Diagnostics;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Worker.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 6: the relay touches its heartbeat file (<c>/tmp/heartbeat-relay</c> in the container) at least every 10
/// seconds of its clock in each state, so
/// the Compose health check's 30-second threshold only trips on a hung relay (AD-25).
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayHeartbeatTests(OutboxFixture outbox)
{
    public enum RelayState
    {
        IdleLeader,
        NotLeader,
        BackingOff,
    }

    [Theory]
    [InlineData(RelayState.IdleLeader)]
    [InlineData(RelayState.NotLeader)]
    [InlineData(RelayState.BackingOff)]
    public async Task Relay_EachTenSecondsOfItsClock_TouchesTheHeartbeatFile(RelayState state)
    {
        RelayClock clock = OutboxFixture.NewClock();
        // An earlier run of this theory leaves the file dated ahead of real time.
        File.Delete(OutboxRelay.HeartbeatPath);
        await using var lockHolder = new NpgsqlConnection(outbox.LockHolderConnectionString);
        if (state == RelayState.NotLeader)
        {
            await lockHolder.OpenAsync(TestContext.Current.CancellationToken);
            await using var take = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", lockHolder);
            take.Parameters.Add(new NpgsqlParameter<long>("key", OutboxRelay.LockKey));
            await take.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Backing off: nothing listens on port 1, so every loop fails to reach Postgres and waits.
        string connectionString = state == RelayState.BackingOff ? "Host=127.0.0.1;Port=1;Username=unused;Password=unused" : outbox.ConnectionString;
        await using RelayHost host = await RelayHost.StartAsync(clock, connectionString, outbox.BootstrapServers);

        for (int step = 0; step < 4; step++)
        {
            // Moves the clock only while the relay waits, and returns once it waits again: each wait follows a touch.
            await clock.StepAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(clock.GetUtcNow().UtcDateTime, HeartbeatAt());
        }
    }

    [Fact]
    public async Task Relay_SendHeldNearItsDeliveryTimeout_HeartbeatIsNeverOlderThanOneSend()
    {
        // The test clock follows real time here, because librdkafka times the send out on its own, real, clock.
        FakeTimeProvider clock = OutboxFixture.NewClock();
        DateTime start = clock.GetUtcNow().UtcDateTime;
        // The theory above leaves the file dated ahead of real time, which would pass the first wait below at once.
        File.Delete(OutboxRelay.HeartbeatPath);
        // At least one due row; rows other tests left Pending may be sent first, and any one send will do.
        await outbox.RaiseAsync(clock.GetUtcNow());
        using var stopTicking = new CancellationTokenSource();
        Task ticking = OutboxFixture.FollowRealTimeAsync(clock, stopTicking.Token);

        // The batch's query waits 4 s on a table lock (under its 5 s command timeout) before it reaches the send, and the
        // broker is paused by then, so the send then waits out its whole 10-second delivery timeout. The broker runs while
        // the relay starts, so its start-up topic step is done once it holds the outbox lock.
        await using var tableLock = new NpgsqlConnection(outbox.ConnectionString);
        await tableLock.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await tableLock.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var lockTable = new NpgsqlCommand("""LOCK TABLE "OutboxMessages" IN ACCESS EXCLUSIVE MODE""", tableLock, transaction))
        {
            await lockTable.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        TimeSpan oldest = TimeSpan.Zero;
        bool paused = false;
        try
        {
            await using RelayHost host = await RelayHost.StartAsync(clock, outbox.ConnectionString, outbox.BootstrapServers);
            await Eventually.UntilAsync(() => HeartbeatAt() >= start, OutboxFixture.WaitLimit);
            await Eventually.UntilAsync(RelayHoldsTheLockAsync, OutboxFixture.WaitLimit);
            var held = Stopwatch.StartNew();
            await outbox.PauseBrokerAsync();
            paused = true;
            bool released = false;
            while (host.ProducerSpans.Count == 0)
            {
                if (!released && held.Elapsed >= TimeSpan.FromSeconds(4))
                {
                    await transaction.RollbackAsync(TestContext.Current.CancellationToken);
                    released = true;
                }

                Assert.True(held.Elapsed < TimeSpan.FromSeconds(30), "The send never ended.");
                TimeSpan age = clock.GetUtcNow().UtcDateTime - (HeartbeatAt() ?? start);
                oldest = age > oldest ? age : oldest;
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            if (paused)
            {
                await outbox.UnpauseBrokerAsync();
            }
        }

        await stopTicking.CancelAsync();
        await ticking;
        // One send's 10-second delivery timeout, plus up to a second for librdkafka's once-a-second timeout scan, plus a
        // second for thread scheduling on a loaded machine (11.1 s was seen once against an 11-second bound). Real time,
        // not the fake clock, sets the send's length, so the bound needs that margin. Without the touch just before the
        // send, the 4-second table-lock wait adds to the age (about 14 s), which this bound still fails.
        Assert.InRange(oldest, TimeSpan.Zero, TimeSpan.FromSeconds(12));
    }

    /// <summary>True once a session holds the relay's advisory lock (PostgreSQL, pg_locks).</summary>
    private async Task<bool> RelayHoldsTheLockAsync()
    {
        await using var connection = new NpgsqlConnection(outbox.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var held = new NpgsqlCommand(
            "SELECT count(*) > 0 FROM pg_locks WHERE locktype = 'advisory' AND objid::bigint = @key AND granted", connection);
        held.Parameters.Add(new NpgsqlParameter<long>("key", OutboxRelay.LockKey));
        return await held.ExecuteScalarAsync(TestContext.Current.CancellationToken) is true;
    }

    private static DateTime? HeartbeatAt() =>
        File.Exists(OutboxRelay.HeartbeatPath) ? File.GetLastWriteTimeUtc(OutboxRelay.HeartbeatPath) : null;
}
