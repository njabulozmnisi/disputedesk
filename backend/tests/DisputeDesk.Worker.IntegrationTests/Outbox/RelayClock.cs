using Microsoft.Extensions.Time.Testing;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// A fake clock that also records the instant of the last wait started on it. <see cref="FakeTimeProvider.Advance"/>
/// moves the time first and then ends the waits that are due, so a <c>Task.Delay</c> the relay starts just after the test
/// moved the clock is due relative to the new time and never ends. <see cref="StepAsync"/> therefore moves the clock only
/// while the relay is waiting at the current instant.
/// </summary>
public sealed class RelayClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private long _lastWaitStartedTicks = -1;

    /// <summary>True when the relay's latest wait started at the current instant, so moving the clock on ends it.</summary>
    public bool RelayWaitsNow => Interlocked.Read(ref _lastWaitStartedTicks) == GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Exchange(ref _lastWaitStartedTicks, GetUtcNow().UtcTicks);
        return timer;
    }

    /// <summary>
    /// Waits until the relay is waiting, moves the clock on by <paramref name="by"/> (at least the relay's wait), then
    /// waits until the relay has run its loop and is waiting again at the new instant.
    /// </summary>
    public async Task StepAsync(TimeSpan by)
    {
        await OutboxFixture.WaitUntilAsync(() => RelayWaitsNow);
        Advance(by);
        await OutboxFixture.WaitUntilAsync(() => RelayWaitsNow);
    }
}
