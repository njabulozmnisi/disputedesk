using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Outbox;

namespace DisputeDesk.Worker;

/// <summary>Composes the Worker's generic host: telemetry, the database and the outbox relay.</summary>
public static class WorkerHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.AddDisputeDeskTelemetry("disputedesk-worker", typeof(WorkerHost).Assembly);
        builder.Services.AddInfrastructure();
        builder.Services.AddOptions<KafkaOptions>()
            .BindConfiguration(KafkaOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddHostedService<OutboxRelay>();
        return builder;
    }
}
