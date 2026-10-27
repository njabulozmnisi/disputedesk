using System.Text.Json;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Infrastructure.Seeding;

/// <summary>
/// Seeds the demo data from <c>fixtures/local.json</c> when <c>Seed:Enabled</c> is on (AD-30). EF Core calls it after
/// every migrate, inside its migration lock (Microsoft Learn: "Data Seeding", <c>UseSeeding</c> and
/// <c>UseAsyncSeeding</c>), so it only adds what is missing. Today that is the customer links: each customer marked
/// <c>linked</c> is linked to their fixture login (<c>LoginSub</c> = the login's <c>userId</c>).
/// </summary>
public sealed partial class Seeder(IOptions<SeedOptions> options, TimeProvider time, ILogger<Seeder> logger)
{
    // Seeding also runs after a downgrade (Data Seeding docs). Below this migration there is no table to seed.
    private const string CustomerLinksMigration = "20261027103742_AddCustomerLinks";

    public async Task SeedAsync(DbContext db, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !(await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains(CustomerLinksMigration))
        {
            return;
        }

        List<CustomerLink> existing = await db.Set<CustomerLink>().AsNoTracking().ToListAsync(cancellationToken);
        AddMissingLinks(db, existing);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The same as <see cref="SeedAsync"/>, for EF Core's tools and bundles, which call the synchronous delegate.</summary>
    public void Seed(DbContext db)
    {
        if (!options.Value.Enabled || !db.Database.GetAppliedMigrations().Contains(CustomerLinksMigration))
        {
            return;
        }

        List<CustomerLink> existing = [.. db.Set<CustomerLink>().AsNoTracking()];
        AddMissingLinks(db, existing);
        db.SaveChanges();
    }

    private void AddMissingLinks(DbContext db, List<CustomerLink> existing)
    {
        Fixtures fixtures = ReadFixtures(options.Value.FixturesPath);
        DateTimeOffset now = time.GetUtcNow();

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

    // A small local file, read synchronously so the sync and async paths share one method.
    private static Fixtures ReadFixtures(string path) =>
        JsonSerializer.Deserialize<Fixtures>(File.ReadAllText(path), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The fixtures file holds no fixtures.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} customer links")]
    private static partial void LogSeeded(ILogger logger, int count);

    // The parts of fixtures/local.json the seeder reads; everything else in the file is ignored.
    private sealed record Fixtures(IReadOnlyList<FixtureCustomer> Customers, IReadOnlyList<FixtureLogin> Logins);

    private sealed record FixtureCustomer(string CustomerId, bool Linked, string? Login);

    private sealed record FixtureLogin(string Key, string UserId);
}
