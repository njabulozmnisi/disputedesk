using DisputeDesk.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DisputeDesk.Worker.IntegrationTests;

public class WorkerHostTests
{
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Worker_host_starts_and_stops_cleanly()
    {
        // Development turns on ValidateOnBuild and ValidateScopes, so a broken registration fails here.
        using var host = WorkerHost.CreateBuilder(["--environment", "Development"]).Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stopTimeout.CancelAfter(ShutdownBudget);

        await host.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);

        await host.StopAsync(stopTimeout.Token);
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.False(stopTimeout.IsCancellationRequested, "The Worker did not stop within its shutdown budget.");
    }

    [Fact]
    public void Worker_host_registers_the_clock_and_the_unit_of_work()
    {
        // Building the context does not open a connection, so the database never needs to exist.
        using var host = WorkerHost.CreateBuilder(
            ["--environment", "Development", "--ConnectionStrings:DisputeDesk", "Host=localhost;Database=unused"]).Build();

        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IAppDbContext>());
    }
}
