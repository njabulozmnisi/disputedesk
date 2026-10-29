using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Makes Postgres itself refuse the <c>AuditLogEntries</c> insert: the statement is pointed at a table that does not
/// exist. Only the failure is staged; every other statement, and the save that fails, are real.
/// </summary>
public sealed class FailAuditInsertInterceptor : DbCommandInterceptor
{
    private const string AuditInsert = "INSERT INTO \"AuditLogEntries\"";

    /// <summary>While armed, every audit insert fails. A test disarms it to let an earlier step through.</summary>
    public bool Armed { get; set; } = true;

    public int Failed { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Break(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Break(command);
        return ValueTask.FromResult(result);
    }

    private void Break(DbCommand command)
    {
        if (!Armed || !command.CommandText.Contains(AuditInsert, StringComparison.Ordinal))
        {
            return;
        }

        command.CommandText = command.CommandText.Replace(AuditInsert, "INSERT INTO \"AuditLogEntriesThatDoNotExist\"", StringComparison.Ordinal);
        Failed++;
    }
}
