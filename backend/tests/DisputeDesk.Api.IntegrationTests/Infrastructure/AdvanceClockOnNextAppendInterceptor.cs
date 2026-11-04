using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Once armed, moves <paramref name="clock"/> on by <paramref name="by"/> as the next save that inserts a stored event
/// runs, once. A handler that read the clock again after that save, on its retry for example, would see the later instant
/// (AD-15: the retry keeps the request's one clock reading). Armed by the test after its own setup saves.
/// </summary>
public sealed class AdvanceClockOnNextAppendInterceptor(TestClock clock, TimeSpan by) : DbCommandInterceptor
{
    private const string StoredEventInsert = "INSERT INTO \"StoredEvents\"";

    private int _armed;

    /// <summary>Moves the clock on as the next save that inserts a stored event runs.</summary>
    public void Arm() => Volatile.Write(ref _armed, 1);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        AdvanceIfArmed(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AdvanceIfArmed(command);
        return ValueTask.FromResult(result);
    }

    private void AdvanceIfArmed(DbCommand command)
    {
        if (command.CommandText.Contains(StoredEventInsert, StringComparison.Ordinal) && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            clock.Advance(by);
        }
    }
}
