using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// One real PostgreSQL container for the whole test run. Never EF InMemory, never SQLite. Its main database is
/// migrated once up front, so tests never wait for an Api instance's start-up migration.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using AppDbContext db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>
    /// Creates a new, empty database on the same server and returns its connection string. Its connections are not
    /// pooled: a pool per test database would keep idle connections open for minutes after the test, and enough of
    /// them exhaust the server's <c>max_connections</c>.
    /// </summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        string name = $"empty_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    /// <summary>Creates a new database with every migration applied, for a test that needs its own rows, and returns its connection string.</summary>
    public async Task<string> CreateMigratedDatabaseAsync()
    {
        string connectionString = await CreateEmptyDatabaseAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();
        return connectionString;
    }
}
