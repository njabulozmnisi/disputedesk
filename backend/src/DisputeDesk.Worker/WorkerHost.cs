using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.Observability;

namespace DisputeDesk.Worker;

/// <summary>Composes the Worker's generic host: telemetry, the database, the outbox relay and the SLA sweep.</summary>
public static class WorkerHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.AddDisputeDeskTelemetry("disputedesk-worker", typeof(WorkerHost).Assembly);
        builder.Services.AddInfrastructure();
        builder.Services.AddOutboxRelayAndSlaSweep();
        return builder;
    }
}
