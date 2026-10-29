using DisputeDesk.Application.Disputes.RaiseDispute;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Just before the raise's one save (the first save that completes an idempotency key), gives the key's row a new lease
/// on another connection, as a request taking the key over would. Only the take-over is staged; the conditional
/// update, the failed save and its rollback are real.
/// </summary>
public sealed class TakeOverLeaseInterceptor : SaveChangesInterceptor
{
    public int TakenOver { get; private set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not DbContext context
            || !context.ChangeTracker.Entries<IdempotencyKey>().Any(entry => entry.State == EntityState.Modified)
            || TakenOver > 0)
        {
            return result;
        }

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""UPDATE "IdempotencyKeys" SET "LeaseId" = gen_random_uuid() WHERE "Status" = 'InProgress'""", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        TakenOver++;
        return result;
    }
}
