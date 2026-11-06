using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisputeDesk.Api.Health;

/// <summary>
/// Applies the EF Core migrations when the Api starts (AD-22), then the seed when <c>Seed:Enabled</c> is on (AD-30),
/// and keeps readiness unhealthy until both are in. EF Core takes its migration lock, so two Api instances starting
/// together apply each migration once. It runs in the background and retries, so liveness answers while Postgres is
/// still unreachable. Locally, where the Api also runs the relay and the sweep, Program calls <see cref="MigrateAsync"/>
/// before the host starts instead, and the background run finds the work done.
/// </summary>
public sealed partial class DatabaseMigrator(IServiceScopeFactory scopes, TimeProvider time, ILogger<DatabaseMigrator> logger)
    : BackgroundService, IHealthCheck
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private volatile bool _migrated;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_migrated ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Migrations are not applied yet."));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!_migrated)
        {
            try
            {
                await MigrateAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogMigrationFailed(logger, exception);
                await Task.Delay(RetryDelay, time, stoppingToken);
            }
        }
    }

    /// <summary>Applies the migrations and the seed once; readiness turns healthy when it returns.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        // EF Core runs the seeding delegates (SeedRegistration) at the end of MigrateAsync, inside its migration lock.
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
        _migrated = true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Applying the database migrations or the seed failed; retrying")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);
}
