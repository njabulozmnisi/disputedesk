using DisputeDesk.Application.Disputes.RaiseDispute;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Throws once right after the raise's one save has committed (EF Core calls <c>SavedChangesAsync</c> after the
/// commit), so the request ends in a 500 although its dispute and completed key are in the database.
/// </summary>
public sealed class FailAfterCommitInterceptor : SaveChangesInterceptor
{
    public int Failed { get; private set; }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        bool completedAKey = eventData.Context?.ChangeTracker.Entries<IdempotencyKey>()
            .Any(entry => entry.Entity.Status == IdempotencyKeyStatus.Completed) == true;
        if (!completedAKey || Failed > 0)
        {
            return ValueTask.FromResult(result);
        }

        Failed++;
        throw new InvalidOperationException("Staged failure after the raise committed.");
    }
}
