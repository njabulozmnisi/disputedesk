using System.Collections.Concurrent;
using System.Data.Common;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Stages a version clash (AD-15). Each armed stored event is committed, with its <c>Disputes</c> row moved to its
/// status and version, in a transaction of its own just before the next save that inserts a stored event runs, as
/// another action committing first would. That save then appends the same version and loses. Only the order is
/// staged; the save that loses and the unique index that refuses it are real. Saves after the armed ones pass.
/// <para>
/// Assumes EF Core sends the whole save as one batched command, one roundtrip (EF Core docs, Efficient Updating:
/// batching), so the command holding the stored-event insert is the save's first and its transaction holds no row
/// lock yet. Were the save split with the <c>Disputes</c> update sent first, the armed commit's own update of that row
/// would wait on the lock the intercepted save holds, and the test would hang rather than clash.
/// </para>
/// </summary>
public sealed class AppendNextVersionInterceptor : DbCommandInterceptor
{
    private const string StoredEventInsert = "INSERT INTO \"StoredEvents\"";

    private readonly ConcurrentQueue<(StoredEvent StoredEvent, DisputeStatus Status)> _armed = new();

    /// <summary>How many armed stored events were committed ahead of a save.</summary>
    public int Appended { get; private set; }

    /// <summary>Arms one more save: <paramref name="storedEvent"/> commits first and moves the row to <paramref name="status"/>.</summary>
    public void Arm(StoredEvent storedEvent, DisputeStatus status) => _armed.Enqueue((storedEvent, status));

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await AppendArmedAsync(command, eventData, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await AppendArmedAsync(command, eventData, cancellationToken);
        return result;
    }

    private async Task AppendArmedAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        if (!command.CommandText.Contains(StoredEventInsert, StringComparison.Ordinal) || !_armed.TryDequeue(out (StoredEvent StoredEvent, DisputeStatus Status) armed))
        {
            return;
        }

        string connectionString = eventData.Context?.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The intercepted command has no context.");
        // A context of its own, with no interceptor and no customer, on its own connection and transaction.
        await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await using IDbContextTransaction transaction = await other.Database.BeginTransactionAsync(cancellationToken);
        StoredEvent storedEvent = armed.StoredEvent;
        other.Set<StoredEventRow>().Add(StoredEventRows.For(storedEvent.DisputeId, storedEvent));
        await other.SaveChangesAsync(cancellationToken);
        await other.Set<Dispute>().IgnoreQueryFilters()
            .Where(dispute => dispute.Id == storedEvent.DisputeId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(dispute => dispute.Status, armed.Status)
                    .SetProperty(dispute => dispute.Version, storedEvent.Version),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Appended++;
    }
}
