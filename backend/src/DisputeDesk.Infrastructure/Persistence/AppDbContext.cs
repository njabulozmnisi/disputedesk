using System.Diagnostics;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
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

        // EF Core advises the same filter on the required dependent, through its navigation, so an entry is never
        // reachable when its dispute is filtered out.
        modelBuilder.Entity<TimelineEntry>().HasQueryFilter(
            OwnershipFilter, entry => CurrentCustomerId != null && entry.Dispute.CustomerId == CurrentCustomerId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddRowsForDisputeChanges();
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException exception) when (UniqueViolations.ErrorFor(exception) is Error error)
        {
            // The lost save committed nothing, but EF Core still tracks its rows (the dispute with its timeline, audit
            // and outbox rows as Added, a completed idempotency key as Modified), so a later save on this context would
            // write them. Clearing the tracker forgets them all (EF Core docs, ChangeTracker.Clear: preferred over
            // detaching each entity).
            ChangeTracker.Clear();
            throw new UniqueViolationException(error, exception);
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddRowsForDisputeChanges();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (UniqueViolations.ErrorFor(exception) is Error error)
        {
            // As in SaveChanges above.
            ChangeTracker.Clear();
            throw new UniqueViolationException(error, exception);
        }
    }

    /// <summary>
    /// AD-14: each pending dispute change gets one audit entry and one outbox row, which EF Core then commits in the
    /// same transaction as the change. The audit log is append-only, so an updated or deleted entry stops the commit.
    /// </summary>
    private void AddRowsForDisputeChanges()
    {
        bool auditEntryChanged = ChangeTracker.Entries<AuditLogEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (auditEntryChanged)
        {
            throw new InvalidOperationException("Audit log entries are append-only: one was modified or deleted.");
        }

        Activity? activity = Activity.Current;
        // Copied first: adding the rows below changes what the change tracker holds.
        List<EntityEntry<Dispute>> disputes = [.. ChangeTracker.Entries<Dispute>()];
        foreach (Dispute dispute in disputes.Select(entry => entry.Entity))
        {
            foreach (DisputeChange change in dispute.TakePendingChanges())
            {
                Set<AuditLogEntry>().Add(CommitRows.AuditEntry(dispute, change, activity));
                Set<OutboxMessage>().Add(CommitRows.OutboxMessage(dispute, change, activity));
            }
        }
    }
}
