using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Seeding;

/// <summary>The Api with <c>Seed:Enabled</c> on, and what the seeded fixture disputes should add.</summary>
public static class SeededApi
{
    /// <summary>The Api seeding <paramref name="connectionString"/> from <paramref name="fixturesPath"/> (the repository's fixtures when null).</summary>
    public static ApiFactory Create(
        string connectionString, TimeProvider? clock = null, string? fixturesPath = null, Action<IServiceCollection>? configureServices = null) =>
        new(
            connectionString,
            configureServices: configureServices,
            settings: new Dictionary<string, string?>
            {
                ["Seed:Enabled"] = "true",
                ["Seed:FixturesPath"] = fixturesPath ?? FixturesFile.FindPath(),
            },
            clock: clock);

    /// <summary>The Api on a new empty database, once it is migrated and seeded (readiness waits for both).</summary>
    public static async Task<(ApiFactory Factory, string ConnectionString)> StartAsync(PostgresFixture postgres, TimeProvider clock)
    {
        string connectionString = await postgres.CreateEmptyDatabaseAsync();
        ApiFactory factory = Create(connectionString, clock);
        using HttpClient client = factory.CreateClient();
        await HealthTests.WaitUntilReadyAsync(client);
        return (factory, connectionString);
    }

    /// <summary>Migrates (and so seeds) through the Api's own registered context, as a restart would.</summary>
    public static async Task MigrateAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A context with no customer, for a test that reads every row past the ownership filter.</summary>
    public static AppDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>The number of fixture disputes.</summary>
    public static int FixtureDisputeCount => FixtureDisputes().Length;

    /// <summary>The number of actions over every fixture dispute: one stored event and one outbox row each.</summary>
    public static int FixtureActionCount => FixtureDisputes().Sum(dispute => dispute.GetProperty("actions").GetArrayLength());

    /// <summary>The rows of the tables seeding may write, and of the audit log it must not.</summary>
    public static async Task<SeededCounts> CountAsync(string connectionString)
    {
        await using AppDbContext db = Open(connectionString);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        return new SeededCounts(
            await db.Set<Dispute>().IgnoreQueryFilters().CountAsync(cancellationToken),
            await db.Set<StoredEventRow>().IgnoreQueryFilters().CountAsync(cancellationToken),
            await db.Set<OutboxMessage>().CountAsync(cancellationToken),
            await db.Set<AuditLogEntry>().CountAsync(cancellationToken));
    }

    /// <summary>What a full seed of the repository's fixtures writes: every dispute, and one stored event and outbox row per action.</summary>
    public static SeededCounts FullSeed => new(FixtureDisputeCount, FixtureActionCount, FixtureActionCount, AuditLogEntries: 0);

    /// <summary>The fixture disputes as the repository's fixtures file writes them.</summary>
    public static JsonElement[] FixtureDisputes()
    {
        using JsonDocument fixtures = FixturesFile.Read();
        return [.. fixtures.RootElement.GetProperty("disputes").EnumerateArray().Select(dispute => dispute.Clone())];
    }
}

public sealed record SeededCounts(int Disputes, int StoredEvents, int OutboxMessages, int AuditLogEntries);
