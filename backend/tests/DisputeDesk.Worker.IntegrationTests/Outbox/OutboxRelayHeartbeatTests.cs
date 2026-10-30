using System.Diagnostics;
using DisputeDesk.Infrastructure.Outbox;
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
        FakeTimeProvider clock = OutboxFixture.NewClock();
        await using var lockHolder = new NpgsqlConnection(outbox.LockHolderConnectionString);
        if (state == RelayState.NotLeader)
        {
            await lockHolder.OpenAsync(TestContext.Current.CancellationToken);
            await using var take = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", lockHolder);
            take.Parameters.Add(new NpgsqlParameter<long>("key", OutboxRelay.LockKey));
            await take.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        string connectionString = state == RelayState.BackingOff ? "" : outbox.ConnectionString;
        await using RelayHost host = await RelayHost.StartAsync(clock, connectionString, outbox.BootstrapServers);

        for (int step = 0; step < 4; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await OutboxFixture.WaitUntilAsync(() => HeartbeatAt() == clock.GetUtcNow().UtcDateTime);
            Assert.Equal(clock.GetUtcNow().UtcDateTime, HeartbeatAt());
        }
    }

    [Fact]
    public async Task Relay_SendHeldNearItsDeliveryTimeout_HeartbeatIsNeverOlderThanOneSend()
    {
        // The test clock follows real time here, because librdkafka times the send out on its own, real, clock.
        FakeTimeProvider clock = OutboxFixture.NewClock();
        DateTime start = clock.GetUtcNow().UtcDateTime;
        // At least one due row; rows other tests left Pending may be sent first, and any one send will do.
        await outbox.RaiseAsync(clock.GetUtcNow());
        using var stopTicking = new CancellationTokenSource();
        Task ticking = TickAsync(clock, stopTicking.Token);

        // The batch's query waits 4 s on a table lock (under its 5 s command timeout) before it reaches the send, and
        // nothing listens on the broker address, so the send then waits out its whole 10-second delivery timeout.
        await using var tableLock = new NpgsqlConnection(outbox.ConnectionString);
        await tableLock.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await tableLock.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var lockTable = new NpgsqlCommand("""LOCK TABLE "OutboxMessages" IN ACCESS EXCLUSIVE MODE""", tableLock, transaction))
        {
            await lockTable.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        TimeSpan oldest = TimeSpan.Zero;
        await using (RelayHost host = await RelayHost.StartAsync(clock, outbox.ConnectionString, "127.0.0.1:1"))
        {
            await OutboxFixture.WaitUntilAsync(() => HeartbeatAt() >= start);
            var held = Stopwatch.StartNew();
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

        await stopTicking.CancelAsync();
        await ticking;
        // One send's 10-second delivery timeout, plus up to a second for librdkafka's once-a-second timeout scan.
        Assert.InRange(oldest, TimeSpan.Zero, TimeSpan.FromSeconds(11));
    }

    /// <summary>Moves the test clock on with real time, 100 ms at a time, until stopped.</summary>
    private static async Task TickAsync(FakeTimeProvider clock, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private static DateTime? HeartbeatAt() =>
        File.Exists(OutboxRelay.HeartbeatPath) ? File.GetLastWriteTimeUtc(OutboxRelay.HeartbeatPath) : null;
}
