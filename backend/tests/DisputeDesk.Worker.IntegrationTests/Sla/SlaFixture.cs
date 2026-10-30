using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// One real PostgreSQL for the SLA sweep tests. The sweep changes every due dispute it can see, so each test runs on a
/// migrated database of its own (<see cref="CreateMigratedDatabaseAsync"/>), as the Api tests' per-test databases do.
/// </summary>
public sealed class SlaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>
    /// A new database with every migration applied; returns its connection string. Unpooled, so a finished test leaves
    /// no idle connections behind to exhaust the server's <c>max_connections</c>.
    /// </summary>
    public async Task<string> CreateMigratedDatabaseAsync()
    {
        string name = $"sla_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        string connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();
        return connectionString;
    }
}

/// <summary>
/// The SLA sweep tests run one at a time, after the parallel tests: the heartbeat file is one path and the span listener
/// is process-wide (xUnit, Running tests in parallel).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SlaTestGroup : ICollectionFixture<SlaFixture>
{
    public const string Name = "SLA sweep";
}
