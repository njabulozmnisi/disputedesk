using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>Runs a test's step just before, or just after, each claim query the sweep sends (the one with <c>SKIP LOCKED</c>).</summary>
public sealed class ClaimHook : DbCommandInterceptor
{
    public Func<Task>? BeforeClaim { get; init; }

    public Func<Task>? AfterClaim { get; init; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (IsClaim(command) && BeforeClaim is not null)
        {
            await BeforeClaim();
        }

        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (IsClaim(command) && AfterClaim is not null)
        {
            await AfterClaim();
        }

        return result;
    }

    private static bool IsClaim(DbCommand command) => command.CommandText.Contains("SKIP LOCKED", StringComparison.Ordinal);
}

/// <summary>
/// Makes the server refuse the stream load of <see cref="DisputeId"/> (a division by zero), as a failed read would be
/// refused. PostgreSQL then aborts the transaction the load runs in (EF Core, Interceptors: modifying the command).
/// </summary>
public sealed class FailStreamLoad : DbCommandInterceptor
{
    public Guid? DisputeId { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        bool isTheLoad = command.CommandText.Contains("FROM \"StoredEvents\"", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == DisputeId);
        if (isTheLoad)
        {
            command.CommandText = $"SELECT * FROM ({command.CommandText}) AS failing WHERE 1 / 0 = 1";
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>Keeps the text of every reader command the sweep sends, in order.</summary>
public sealed class CommandLog : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
            {
                return [.. _commands];
            }
        }
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        lock (_commands)
        {
            _commands.Add(command.CommandText);
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>Counts the transactions the sweep commits and the savepoints EF Core creates inside them.</summary>
public sealed class TransactionCounter : DbTransactionInterceptor
{
    private int _committed;
    private int _savepoints;

    public int Committed => _committed;

    public int Savepoints => _savepoints;

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _committed);
        return Task.CompletedTask;
    }

    public override Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _savepoints);
        return Task.CompletedTask;
    }
}

/// <summary>Holds the first <c>parties</c> claims, each just after it returned, until all of them have arrived.</summary>
public sealed class ClaimRendezvous(int parties)
{
    private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public ClaimHook Hook => new() { AfterClaim = ArriveAsync };

    private Task ArriveAsync()
    {
        int arrived = Interlocked.Increment(ref _arrived);
        if (arrived == parties)
        {
            _all.SetResult();
        }

        return arrived <= parties ? _all.Task.WaitAsync(TimeSpan.FromSeconds(30)) : Task.CompletedTask;
    }
}
