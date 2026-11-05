using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Infrastructure.Seeding;

/// <summary>
/// Seeds the demo data from <c>backend/fixtures/local.json</c> when <c>Seed:Enabled</c> is on (AD-30). EF Core calls it after
/// every migrate (Microsoft Learn: "Data Seeding", <c>UseSeeding</c> and <c>UseAsyncSeeding</c>), so it only adds what
/// is missing. First the customer links: each customer marked <c>linked</c> is linked to their fixture login
/// (<c>LoginSub</c> = the login's <c>userId</c>). Then the disputes: the migrating context reads each fixture dispute
/// past the ownership filter, AD-11's third named bypass, by its customer as well as its transaction
/// (<see cref="SeedDispute.Matches"/>), and raises only the missing ones, each committed whole through the AD-14 commit
/// in one <c>SaveChanges</c>.
/// <para>
/// The writes run on the migrating context's connection, in the transaction EF Core holds there when no migration was
/// pending. Inside that transaction each <c>SaveChanges</c> is its own savepoint (EF Core docs, Transactions:
/// savepoints), so a dispute's stream is still all or nothing. The unique <c>(AccountRef, TransactionId)</c> index
/// refuses a duplicate.
/// </para>
/// </summary>
public sealed partial class Seeder(IOptions<SeedOptions> options, TimeProvider time, ILogger<Seeder> logger)
{
    // Seeding also runs after a downgrade (Data Seeding docs). Below each migration there is no table to seed.
    private const string CustomerLinksMigration = "20261027103742_AddCustomerLinks";
    private const string DisputesMigration = "20261028084134_AddDisputes";

    public async Task SeedAsync(DbContext db, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        List<string> applied = [.. await db.Database.GetAppliedMigrationsAsync(cancellationToken)];
        if (!applied.Contains(CustomerLinksMigration))
        {
            return;
        }

        var fixtures = Fixtures.Read(options.Value.FixturesPath);
        DateTimeOffset now = SeedingInstant();
        List<CustomerLink> existing = await db.Set<CustomerLink>().AsNoTracking().ToListAsync(cancellationToken);
        AddMissingLinks(db, fixtures, existing, now);
        await db.SaveChangesAsync(cancellationToken);

        if (!applied.Contains(DisputesMigration))
        {
            return;
        }

        // Every fixture dispute is checked before any is written, then each is seeded in file order.
        List<SeedDispute> planned = SeedDispute.Plan(fixtures, now);
        int added = 0;
        foreach (SeedDispute dispute in planned)
        {
            bool everDisputed = await db.Set<Dispute>().IgnoreQueryFilters([AppDbContext.OwnershipFilter]).AnyAsync(dispute.Matches(), cancellationToken);
            if (everDisputed)
            {
                continue;
            }

            db.Set<Dispute>().Add(dispute.Write(everDisputed));
            await db.SaveChangesAsync(cancellationToken);
            added++;
        }

        LogSeededDisputes(logger, added);
    }

    /// <summary>The same as <see cref="SeedAsync"/>, for EF Core's tools and bundles, which call the synchronous delegate.</summary>
    public void Seed(DbContext db)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        List<string> applied = [.. db.Database.GetAppliedMigrations()];
        if (!applied.Contains(CustomerLinksMigration))
        {
            return;
        }

        var fixtures = Fixtures.Read(options.Value.FixturesPath);
        DateTimeOffset now = SeedingInstant();
        List<CustomerLink> existing = [.. db.Set<CustomerLink>().AsNoTracking()];
        AddMissingLinks(db, fixtures, existing, now);
        db.SaveChanges();

        if (!applied.Contains(DisputesMigration))
        {
            return;
        }

        // Every fixture dispute is checked before any is written, then each is seeded in file order.
        List<SeedDispute> planned = SeedDispute.Plan(fixtures, now);
        int added = 0;
        foreach (SeedDispute dispute in planned)
        {
            bool everDisputed = db.Set<Dispute>().IgnoreQueryFilters([AppDbContext.OwnershipFilter]).Any(dispute.Matches());
            if (everDisputed)
            {
                continue;
            }

            db.Set<Dispute>().Add(dispute.Write(everDisputed));
            db.SaveChanges();
            added++;
        }

        LogSeededDisputes(logger, added);
    }

    private void AddMissingLinks(DbContext db, Fixtures fixtures, List<CustomerLink> existing, DateTimeOffset now)
    {
        int added = 0;
        foreach (FixtureCustomer customer in fixtures.Customers.Where(customer => customer.Linked))
        {
            string loginSub = fixtures.Logins.Single(login => login.Key == customer.Login).UserId;
            // Either side already linked (seeded before, or linked by hand) leaves it alone, so a restart never fails on a unique index.
            if (existing.Exists(link => link.LoginSub == loginSub || link.CustomerId == customer.CustomerId))
            {
                continue;
            }

            db.Set<CustomerLink>().Add(new CustomerLink
            {
                Id = Guid.CreateVersion7(now),
                LoginSub = loginSub,
                CustomerId = customer.CustomerId,
                LinkedAtUtc = now,
            });
            added++;
        }

        LogSeeded(logger, added);
    }

    // The one clock reading of a seed (AD-4), cut to the whole microseconds a Postgres timestamptz keeps, so the SLA demo
    // dispute's stored instants are exactly the ones resolved from it.
    private DateTimeOffset SeedingInstant() => Instants.ToWholeMicroseconds(time.GetUtcNow());

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} customer links")]
    private static partial void LogSeeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} disputes")]
    private static partial void LogSeededDisputes(ILogger logger, int count);
}
