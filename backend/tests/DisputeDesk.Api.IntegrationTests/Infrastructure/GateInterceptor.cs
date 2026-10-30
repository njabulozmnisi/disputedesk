using System.Data.Common;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Holds every command whose SQL contains <paramref name="marker"/> before it runs, until <see cref="Open"/> is called
/// or the command's cancellation token fires, so a test decides when a conditional statement sees the database. Once
/// open, later commands pass.
/// </summary>
public sealed class GateInterceptor(string marker) : DbCommandInterceptor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Channel<bool> _arrivals = Channel.CreateUnbounded<bool>();
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when <paramref name="commands"/> commands are being held.</summary>
    public async Task WaitForHeldAsync(int commands)
    {
        for (int held = 0; held < commands; held++)
        {
            await _arrivals.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Timeout);
        }
    }

    public void Open() => _open.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(marker, StringComparison.Ordinal) && !_open.Task.IsCompleted)
        {
            _arrivals.Writer.TryWrite(true);
            await _open.Task.WaitAsync(Timeout, cancellationToken);
        }

        return result;
    }
}
