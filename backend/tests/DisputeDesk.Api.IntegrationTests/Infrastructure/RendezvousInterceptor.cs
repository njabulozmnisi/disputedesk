using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Holds the first <paramref name="parties"/> commands whose SQL contains <paramref name="marker"/>, once executed,
/// until all of them have run, so two requests both finish a read before either goes on. Later commands pass.
/// </summary>
public sealed class RendezvousInterceptor(string marker, int parties) : DbCommandInterceptor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (!command.CommandText.Contains(marker, StringComparison.Ordinal))
        {
            return result;
        }

        int arrived = Interlocked.Increment(ref _arrived);
        if (arrived > parties)
        {
            return result;
        }

        if (arrived == parties)
        {
            _allArrived.TrySetResult();
        }

        await _allArrived.Task.WaitAsync(Timeout, cancellationToken);
        return result;
    }
}
