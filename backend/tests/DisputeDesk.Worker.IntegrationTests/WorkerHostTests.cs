using DisputeDesk.Application.Abstractions;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Worker.Sla;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DisputeDesk.Worker.IntegrationTests;

public class WorkerHostTests
{
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task WorkerHost_StartThenStop_CompletesCleanly()
    {
        // Development turns on ValidateOnBuild and ValidateScopes, so a broken registration fails here.
        HostApplicationBuilder builder = WorkerHost.CreateBuilder(["--environment", "Development"]);
        // No SLA sweep: a started host would escalate the due disputes of whatever database its configuration names.
        builder.Services.Remove(builder.Services.Single(service => service.ImplementationType == typeof(SlaSweep)));
        using IHost host = builder.Build();
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stopTimeout.CancelAfter(ShutdownBudget);

        await host.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);

        await host.StopAsync(stopTimeout.Token);
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.False(stopTimeout.IsCancellationRequested, "The Worker did not stop within its shutdown budget.");
    }

    [Fact]
    public void CreateBuilder_Build_RegistersClockAndUnitOfWork()
    {
        // Building the context does not open a connection, so the database never needs to exist.
        using IHost host = WorkerHost.CreateBuilder(
            ["--environment", "Development", "--ConnectionStrings:DisputeDesk", "Host=localhost;Database=unused"]).Build();

        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
        using IServiceScope scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IAppDbContext>());
    }

    [Fact]
    public void CreateBuilder_Build_RunsTheOutboxRelayAndTheSlaSweep()
    {
        using IHost host = WorkerHost.CreateBuilder(["--environment", "Development"]).Build();

        Assert.Equal(
            [typeof(OutboxRelay), typeof(SlaSweep)],
            host.Services.GetServices<IHostedService>().Select(service => service.GetType()).Where(type => type.Namespace?.StartsWith("DisputeDesk.", StringComparison.Ordinal) == true));
    }
}
