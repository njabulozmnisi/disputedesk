using System.Diagnostics;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// The PostgreSQL unit of work. Feature stories add their entity configurations to this assembly. It holds the two
/// rules every dispute query and commit obey: the ownership filter (AD-11) and the one commit path (AD-14).
/// </summary>
/// <param name="options">The context options.</param>
/// <param name="currentCustomer">The request's customer in the Api; absent in the Worker and in tools, where the ownership filter matches nothing.</param>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, CurrentCustomer? currentCustomer = null)
    : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    /// <summary>The name of the ownership filter; only <c>Operations/</c> and <c>Sla/</c> may ignore it (AD-11).</summary>
    public const string OwnershipFilter = "Ownership";

    /// <summary>The Data Protection key ring, shared by every Api instance (AD-7).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<TEntity> Entities<TEntity>() where TEntity : class => Set<TEntity>();

    // The filter's parameter: a member of this context, so EF Core reads it per context instance at query time (EF Core
    // docs, "Global Query Filters"). Null until the Linked policy resolves the customer, never a captured value.
    private string? CurrentCustomerId => currentCustomer?.CustomerIdOrNull;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Dispute>().HasQueryFilter(
            OwnershipFilter, dispute => CurrentCustomerId != null && dispute.CustomerId == CurrentCustomerId);

        // EF Core advises the same filter on the required dependent, through its navigation, so a stored event is never
        // reachable when its dispute is filtered out (EF Core docs, Global Query Filters: required navigations).
        modelBuilder.Entity<StoredEventRow>().HasQueryFilter(
            OwnershipFilter, row => CurrentCustomerId != null && row.Dispute.CustomerId == CurrentCustomerId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            AddRowsForDisputeChanges();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException exception) when (UniqueViolations.ErrorFor(exception) is Error error)
        {
            ChangeTracker.Clear();
            throw new UniqueViolationException(error, exception);
        }
        catch
        {
            // As in SaveChangesAsync below.
            ChangeTracker.Clear();
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            AddRowsForDisputeChanges();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueViolations.ErrorFor(exception) is Error error)
        {
            ChangeTracker.Clear();
            throw new UniqueViolationException(error, exception);
        }
        catch
        {
            // AD-14, failed save: the pending stored events were taken before saving, so the rows of a failed save
            // live only in the change tracker (the dispute and its stored event and outbox rows as Added, a completed
            // idempotency key as Modified). Clearing it forgets them all, so a later save on this context can never
            // write the dispute without its stored event (EF Core docs, ChangeTracker.Clear).
            ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// AD-14: each pending stored event of a tracked dispute becomes one <c>StoredEvents</c> row and, when its type is
    /// published, one outbox row under the same ID, which EF Core then commits in the same transaction as the dispute
    /// (EF Core docs, Transactions: one SaveChanges is one transaction). Stored events and audit entries are insert
    /// only, so a modified or deleted one stops the commit before anything is written. A dispute is written only with
    /// its stored event, so an added or modified dispute with none pending stops it too: the unique version is then the
    /// one concurrency check on every write to <c>Disputes</c> (AD-15, ad-009).
    /// </summary>
    private void AddRowsForDisputeChanges()
    {
        bool recordChanged = ChangeTracker.Entries()
            .Where(entry => entry.Entity is StoredEventRow or AuditLogEntry)
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (recordChanged)
        {
            throw new InvalidOperationException("Stored events and audit log entries are insert only: one was modified or deleted.");
        }

        Activity? activity = Activity.Current;
        // Copied first: adding the rows below changes what the change tracker holds.
        List<EntityEntry<Dispute>> disputes = [.. ChangeTracker.Entries<Dispute>()];
        foreach (EntityEntry<Dispute> entry in disputes)
        {
            Dispute dispute = entry.Entity;
            IReadOnlyList<StoredEvent> storedEvents = dispute.TakePendingChanges();
            if (storedEvents.Count == 0 && entry.State is EntityState.Added or EntityState.Modified)
            {
                throw new InvalidOperationException("A dispute is written only with its stored event: one was added or modified with none pending.");
            }

            foreach (StoredEvent storedEvent in storedEvents)
            {
                // One ID for the stored event, its outbox row and the CloudEvent id: a UUIDv7 from its instant (AD-4).
                var id = Guid.CreateVersion7(storedEvent.OccurredAtUtc);
                Set<StoredEventRow>().Add(StoredEventRow.From(id, dispute, storedEvent, activity?.TraceId.ToHexString()));
                if (CommitRows.OutboxMessage(id, storedEvent, activity) is OutboxMessage message)
                {
                    Set<OutboxMessage>().Add(message);
                }
            }
        }
    }
}
