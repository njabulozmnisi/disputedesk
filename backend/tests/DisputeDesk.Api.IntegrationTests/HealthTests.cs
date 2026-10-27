using System.Net;
using DisputeDesk.Api.Health;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisputeDesk.Api.IntegrationTests;

public class HealthTests(PostgresFixture postgres)
{
    // Nothing listens on port 1, and a one-second timeout keeps the failing check fast.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1";

    [Fact]
    public async Task Live_NoDependencies_Returns200()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ready_PostgresReachable_Returns200()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient client = factory.CreateClient();
        await WaitUntilReadyAsync(client);

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ready_PostgresUnreachable_Returns503()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ready_TwoInstancesOnAnEmptyDatabase_Return200WithTheMigrationApplied()
    {
        string emptyDatabase = await postgres.CreateEmptyDatabaseAsync();
        // Two instances start together on the same empty database: EF Core's migration lock lets one apply it.
        await using var first = new ApiFactory(emptyDatabase);
        await using var second = new ApiFactory(emptyDatabase);
        using HttpClient firstClient = first.CreateClient();
        using HttpClient secondClient = second.CreateClient();

        await WaitUntilReadyAsync(firstClient);
        await WaitUntilReadyAsync(secondClient);

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(emptyDatabase).Options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), "20261026115752_AddSessionsAndDataProtectionKeys");
        // Both tables exist: these queries throw when a table is missing.
        await db.Set<Session>().CountAsync(TestContext.Current.CancellationToken);
        await db.DataProtectionKeys.CountAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CheckHealthAsync_MigrationsNotApplied_ReturnsUnhealthy()
    {
        // The migrator retries against an unreachable server, so its readiness check stays unhealthy.
        await using var factory = new ApiFactory(UnreachableDatabase);

        HealthCheckResult result = await factory.Services.GetRequiredService<DatabaseMigrator>()
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Ready_ZitadelDiscoveryFails_Returns503()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        factory.Zitadel.DiscoveryFails = true;
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static async Task WaitUntilReadyAsync(HttpClient client)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The Api never reported ready.");
    }

    [Fact]
    public async Task Ready_ConnectionStringMissing_Returns503()
    {
        await using var factory = new ApiFactory(connectionString: null);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
