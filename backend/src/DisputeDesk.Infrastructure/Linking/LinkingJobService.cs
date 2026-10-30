using System.Diagnostics;
using System.Threading.Channels;
using DisputeDesk.Application.Linking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// Runs the queued linking jobs, up to 4 at once, each in its own DI scope: the queued background task pattern, a
/// <see cref="BackgroundService"/> reading a bounded <see cref="Channel{T}"/> (Microsoft Learn, "Background tasks with
/// hosted services": queued background tasks). One failed job never stops the next.
/// </summary>
public sealed partial class LinkingJobService(ChannelReader<LinkingJob> jobs, IServiceScopeFactory scopes, ILogger<LinkingJobService> logger) : BackgroundService
{
    private const int JobsAtOnce = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Each job's span is the root of its own trace, linked to its request, never a child of whatever started the host.
        Activity.Current = null;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = JobsAtOnce, CancellationToken = stoppingToken };
        try
        {
            // shortcut: an Api restart drops the queued jobs and the person starts again (AD-12, Restart); a durable queue
            // is the upgrade if restarts during linking become common
            await Parallel.ForEachAsync(jobs.ReadAllAsync(stoppingToken), parallel, RunOneAsync);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    private async ValueTask RunOneAsync(LinkingJob job, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LinkingJobSteps>().RunAsync(job, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Only the type: an exception's message can carry the details the job holds.
            LogJobFailed(logger, job.ChallengeId, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Linking challenge {ChallengeId} stopped on an unexpected {ErrorType}")]
    private static partial void LogJobFailed(ILogger logger, Guid challengeId, string errorType);
}
