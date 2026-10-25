using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.Observability;

namespace DisputeDesk.Worker;

/// <summary>Composes the Worker's generic host. The outbox relay registers here in a later story.</summary>
public static class WorkerHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddDisputeDeskTelemetry("disputedesk-worker", typeof(WorkerHost).Assembly);
        builder.Services.AddInfrastructure();
        return builder;
    }
}
