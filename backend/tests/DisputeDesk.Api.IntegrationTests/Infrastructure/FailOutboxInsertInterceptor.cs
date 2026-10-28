using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Makes Postgres itself refuse the <c>OutboxMessages</c> insert: the statement is pointed at a table that does not
/// exist, so it fails in the database after the <c>Disputes</c> insert of the same save. Only the failure is staged;
/// the transaction, the earlier insert and the rollback are real.
/// </summary>
public sealed class FailOutboxInsertInterceptor : DbCommandInterceptor
{
    private const string DisputesInsert = "INSERT INTO \"Disputes\"";
    private const string OutboxInsert = "INSERT INTO \"OutboxMessages\"";

    private bool _disputesInserted;

    public int Failed { get; private set; }

    /// <summary>Whether the dispute was inserted, in this command or an earlier one, before the failing outbox insert.</summary>
    public bool DisputesInsertedFirst { get; private set; }

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
        string sql = command.CommandText;
        int disputes = sql.IndexOf(DisputesInsert, StringComparison.Ordinal);
        int outbox = sql.IndexOf(OutboxInsert, StringComparison.Ordinal);
        if (disputes >= 0 && (outbox < 0 || disputes < outbox))
        {
            _disputesInserted = true;
        }

        if (outbox < 0)
        {
            return;
        }

        DisputesInsertedFirst = _disputesInserted;
        command.CommandText = sql.Replace(OutboxInsert, "INSERT INTO \"OutboxMessagesThatDoNotExist\"", StringComparison.Ordinal);
        Failed++;
    }
}
