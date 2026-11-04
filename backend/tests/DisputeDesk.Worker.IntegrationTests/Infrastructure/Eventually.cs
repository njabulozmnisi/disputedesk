using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace DisputeDesk.Worker.IntegrationTests.Infrastructure;

/// <summary>The one poll loop of this test project: checks a condition every 50 ms until it holds or the timeout passes.</summary>
public static class Eventually
{
    /// <summary>
    /// Waits until <paramref name="condition"/> holds, or throws <see cref="TimeoutException"/> after
    /// <paramref name="timeout"/>. When <paramref name="clock"/> is given, each attempt moves it on by 1 second, so a
    /// host waiting on that fake clock keeps running its loop.
    /// </summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, FakeTimeProvider? clock = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > timeout)
            {
                throw new TimeoutException($"The condition was not met within {timeout}.");
            }

            clock?.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    public static Task UntilAsync(Func<bool> condition, TimeSpan timeout, FakeTimeProvider? clock = null) =>
        UntilAsync(() => Task.FromResult(condition()), timeout, clock);
}
