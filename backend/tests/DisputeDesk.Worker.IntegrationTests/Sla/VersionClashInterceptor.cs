using System.Data.Common;
using System.Text.Json;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// Stages a version clash for the sweep (AD-15). While the sweep holds a claimed row, no other transaction can append to
/// its stream: the stored event's foreign key needs a lock that <c>FOR UPDATE</c> blocks. So the armed stored event is
/// written inside the sweep's own transaction, just before the dispute's savepoint (so rolling back to it keeps the
/// event), with the <c>Disputes</c> row moved to its status and version; and the sweep's first replay of that dispute
/// reads only the versions before it, as a replay taken just before that action committed would. The save then appends
/// the same version and loses; the unique index that refuses it is real.
/// </summary>
public sealed class VersionClashInterceptor : DbCommandInterceptor, IDbTransactionInterceptor
{
    private StoredEvent? _armed;
    private DisputeStatus _status;
    private int _fired;
    private int _staleReads;

    public bool Fired => _fired == 1;

    /// <summary>Arms the sweep's next dispute: <paramref name="storedEvent"/> lands first and moves the row to <paramref name="status"/>.</summary>
    public void Arm(StoredEvent storedEvent, DisputeStatus status) => (_armed, _status) = (storedEvent, status);

    public async ValueTask<InterceptionResult> CreatingSavepointAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (_armed is not StoredEvent armed || transaction.Connection is not DbConnection connection || Interlocked.Exchange(ref _fired, 1) == 1)
        {
            return result;
        }

        // A context of its own on the sweep's connection and transaction (EF Core, Transactions: sharing a transaction).
        await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        await other.Database.UseTransactionAsync(transaction, cancellationToken);
        other.Set<StoredEventRow>().Add(new StoredEventRow
        {
            Id = Guid.CreateVersion7(armed.OccurredAtUtc),
            DisputeId = armed.DisputeId,
            Version = armed.Version,
            EventType = armed.GetType().Name,
            OccurredAtUtc = armed.OccurredAtUtc,
            ActorType = armed.ActorType,
            ActorId = armed.ActorId,
            Data = JsonSerializer.Serialize(armed, armed.GetType(), DisputeDeskJson.Options),
        });
        await other.SaveChangesAsync(cancellationToken);
        await other.Set<Dispute>().IgnoreQueryFilters([AppDbContext.OwnershipFilter])
            .Where(dispute => dispute.Id == armed.DisputeId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(dispute => dispute.Status, _status).SetProperty(dispute => dispute.Version, armed.Version), cancellationToken);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        bool isTheLoad = _armed is StoredEvent armed && Fired
            && command.CommandText.Contains("FROM \"StoredEvents\"", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == armed.DisputeId);
        if (isTheLoad && Interlocked.Increment(ref _staleReads) == 1)
        {
            command.CommandText = $"""SELECT * FROM ({command.CommandText}) AS stale WHERE stale."Version" < {_armed?.Version} ORDER BY stale."Version" """;
        }

        return ValueTask.FromResult(result);
    }
}
