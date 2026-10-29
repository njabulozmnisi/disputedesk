using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Holds the first command whose SQL contains <paramref name="marker"/>, once executed, until <see cref="Release"/> is
/// called or the command's cancellation token fires (as a request timeout does), so a test can act while a request is
/// mid-way. Later commands pass.
/// </summary>
public sealed class HoldInterceptor(string marker) : DbCommandInterceptor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _seen;

    /// <summary>Completes when the held command has run and is waiting.</summary>
    public Task Reached => _reached.Task;

    public void Release() => _released.TrySetResult();

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (!command.CommandText.Contains(marker, StringComparison.Ordinal) || Interlocked.Increment(ref _seen) != 1)
        {
            return result;
        }

        _reached.TrySetResult();
        await _released.Task.WaitAsync(Timeout, cancellationToken);
        return result;
    }
}
