using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Api.Bff;

/// <summary>
/// Deletes expired session rows once an hour (AD-7). A session that is used again is deleted on that request; this
/// removes the ones nobody comes back to.
/// </summary>
public sealed partial class ExpiredSessionCleanup(IServiceScopeFactory scopes, TimeProvider time, ILogger<ExpiredSessionCleanup> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<Session>()
            .Where(session => session.ExpiresAtUtc <= now || session.AbsoluteExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                int deleted = await DeleteExpiredAsync(stoppingToken);
                LogDeleted(logger, deleted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next tick tries again; one failed sweep must not stop the Api.
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired sessions")]
    private static partial void LogDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Expired session cleanup failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
