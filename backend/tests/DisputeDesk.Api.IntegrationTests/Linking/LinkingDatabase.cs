using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>What the linking job leaves in a test Api's own database: advisory locks, open transactions, audit entries.</summary>
public static class LinkingDatabase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Customer locks in this database, granted (held) or not (waited for): advisory locks in the two-key form whose first
    /// key is the customer lock namespace, 6005. In <c>pg_locks</c> a two-key advisory lock shows its first key as
    /// <c>classid</c> and <c>objsubid</c> 2; a one-key lock shows <c>objsubid</c> 1 (PostgreSQL, pg_locks).
    /// </summary>
    public static Task<int> AdvisoryLocksAsync(this ApiFactory factory, bool granted) =>
        factory.CountAsync(db => db.Database.SqlQuery<int>(
            $"""SELECT count(*)::int AS "Value" FROM pg_locks WHERE locktype = 'advisory' AND classid = 6005 AND objsubid = 2 AND granted = {granted} AND database = (SELECT oid FROM pg_database WHERE datname = current_database())"""));

    /// <summary>Connections to this database inside a transaction but not running a statement.</summary>
    public static Task<int> IdleInTransactionAsync(this ApiFactory factory) =>
        factory.CountAsync(db => db.Database.SqlQuery<int>(
            $"""SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE datname = current_database() AND state LIKE 'idle in transaction%'"""));

    /// <summary>Waits until <paramref name="count"/> jobs wait for a customer lock in this database.</summary>
    public static Task WaitForLockWaitersAsync(this ApiFactory factory, int count) =>
        Eventually.UntilAsync(async () => await factory.AdvisoryLocksAsync(granted: false) >= count, Timeout);

    /// <summary>
    /// Waits until the jobs of <paramref name="logins"/> have recorded <paramref name="count"/> entries whose action is
    /// one of <paramref name="stops"/>, and returns those entries.
    /// </summary>
    public static async Task<List<AuditLogEntry>> WaitForStopsAsync(
        this ApiFactory factory, IReadOnlyCollection<string> logins, IReadOnlyCollection<AuditAction> stops, int count)
    {
        List<AuditLogEntry> entries = [];
        await Eventually.UntilAsync(
            async () =>
            {
                await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
                entries = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>().AsNoTracking()
                    .Where(entry => logins.Contains(entry.ActorId) && stops.Contains(entry.Action))
                    .ToListAsync(TestContext.Current.CancellationToken);
                return entries.Count >= count;
            },
            Timeout);
        return entries;
    }

    private static async Task<int> CountAsync(this ApiFactory factory, Func<AppDbContext, IQueryable<int>> query)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>()).SingleAsync(TestContext.Current.CancellationToken);
    }
}
