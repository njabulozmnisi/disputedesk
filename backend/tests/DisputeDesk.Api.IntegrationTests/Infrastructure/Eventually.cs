using System.Diagnostics;
using System.Net;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>The one poll loop of this test project: checks a condition every 50 ms until it holds or the timeout passes.</summary>
public static class Eventually
{
    /// <summary>Waits until <paramref name="condition"/> holds, or throws <see cref="TimeoutException"/> after <paramref name="timeout"/>.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > timeout)
            {
                throw new TimeoutException($"The condition was not met within {timeout}.");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Waits until the Api answers <c>/health/ready</c> with 200, which it does once its start-up migration has run.</summary>
    public static Task WaitUntilReadyAsync(HttpClient client) =>
        UntilAsync(
            async () =>
            {
                HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
                return response.StatusCode == HttpStatusCode.OK;
            },
            TimeSpan.FromSeconds(20));
}
