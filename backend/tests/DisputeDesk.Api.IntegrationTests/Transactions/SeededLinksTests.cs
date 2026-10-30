using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Seeding;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>
/// AC7: with <c>Seed:Enabled</c>, customers A and B are linked to their fixture logins exactly once; C and D are not.
/// Story 5.4 extends each case to the seeded disputes: every fixture dispute once, with one stored event and one outbox
/// row per action, and no audit entry. EF Core seeds after every migrate, so "the seeder runs twice" is a second migrate.
/// </summary>
public class SeededLinksTests(PostgresFixture postgres)
{
    private static readonly (string LoginSub, string CustomerId)[] SeededLinks =
        [("dd-login-customer-a", "CUS-000001"), ("dd-login-customer-b", "CUS-000002")];

    [Fact]
    public async Task Seeder_FreshStartThenRunAgain_LinksAAndBOnceAndNotCOrD()
    {
        string emptyDatabase = await postgres.CreateEmptyDatabaseAsync();
        await using ApiFactory factory = SeedingApi(emptyDatabase);
        using HttpClient client = factory.CreateClient();
        // Ready means migrated, and with it seeded: the first run.
        await HealthTests.WaitUntilReadyAsync(client);

        await MigrateAsync(factory);

        Assert.Equal(SeededLinks, await LinksAsync(emptyDatabase));
        Assert.Equal(SeededApi.FullSeed, await SeededApi.CountAsync(emptyDatabase));
    }

    [Fact]
    public async Task Seeder_TwoInstancesStartTogetherOnAnEmptyDatabase_LinksAAndBOnce()
    {
        string emptyDatabase = await postgres.CreateEmptyDatabaseAsync();
        await using ApiFactory first = SeedingApi(emptyDatabase);
        await using ApiFactory second = SeedingApi(emptyDatabase);
        using HttpClient firstClient = first.CreateClient();
        using HttpClient secondClient = second.CreateClient();

        await HealthTests.WaitUntilReadyAsync(firstClient);
        await HealthTests.WaitUntilReadyAsync(secondClient);

        Assert.Equal(SeededLinks, await LinksAsync(emptyDatabase));
        Assert.Equal(SeededApi.FullSeed, await SeededApi.CountAsync(emptyDatabase));
    }

    [Fact]
    public async Task Seeder_SynchronousMigrateOnAnEmptyDatabase_LinksAAndBOnce()
    {
        // EF Core's tools and migration bundles call Migrate() and the UseSeeding delegate. The Api's registered
        // context options are pointed at a second empty database, so its own start-up migrate (async) never touches it.
        await using ApiFactory factory = SeedingApi(await postgres.CreateEmptyDatabaseAsync());
        string emptyDatabase = await postgres.CreateEmptyDatabaseAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DbContextOptions<AppDbContext> registered = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();

        MigrateSynchronously(new DbContextOptionsBuilder<AppDbContext>(registered).UseNpgsql(emptyDatabase).Options);

        Assert.Equal(SeededLinks, await LinksAsync(emptyDatabase));
        Assert.Equal(SeededApi.FullSeed, await SeededApi.CountAsync(emptyDatabase));
    }

    // Deliberately synchronous: the path the tools take. A plain method, so nothing nudges it towards MigrateAsync.
    private static void MigrateSynchronously(DbContextOptions<AppDbContext> options)
    {
        using var db = new AppDbContext(options);
        db.Database.Migrate();
    }

    [Fact]
    public async Task Seeder_SeedDisabled_AddsNoLinks()
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using var factory = new ApiFactory(database, settings: new Dictionary<string, string?> { ["Seed:FixturesPath"] = FixturesFile.FindPath() });

        await MigrateAsync(factory);

        Assert.Empty(await LinksAsync(database));
        Assert.Equal(new SeededCounts(0, 0, 0, 0), await SeededApi.CountAsync(database));
    }

    [Fact]
    public async Task Seeder_DowngradeBelowTheCustomerLinksMigration_DoesNotFail()
    {
        // EF Core seeds after a downgrade too; with CustomerLinks gone there is nothing to seed, and no error.
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using ApiFactory factory = SeedingApi(database);
        using HttpClient client = factory.CreateClient();
        // Let the Api's own start-up migrate finish first, so it never races the downgrade for the migration lock.
        await HealthTests.WaitUntilReadyAsync(client);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261026115752_AddSessionsAndDataProtectionKeys", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("20261027103742_AddCustomerLinks", await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Seeder_DowngradeBelowTheDisputesMigration_DoesNotFailAndKeepsTheLinks()
    {
        // Story 5.4: with Disputes gone the seeder still seeds the links, and skips the disputes without an error.
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using ApiFactory factory = SeedingApi(database);
        using HttpClient client = factory.CreateClient();
        await HealthTests.WaitUntilReadyAsync(client);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261027103742_AddCustomerLinks", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("20261028084134_AddDisputes", await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(SeededLinks, await LinksAsync(database));
    }

    private static ApiFactory SeedingApi(string connectionString) => new(connectionString, settings: new Dictionary<string, string?>
    {
        ["Seed:Enabled"] = "true",
        ["Seed:FixturesPath"] = FixturesFile.FindPath(),
    });

    private static async Task MigrateAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<(string LoginSub, string CustomerId)>> LinksAsync(string connectionString)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        List<CustomerLink> links = await db.Set<CustomerLink>().OrderBy(link => link.CustomerId).ToListAsync(TestContext.Current.CancellationToken);
        return [.. links.Select(link => (link.LoginSub, link.CustomerId))];
    }
}
