using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Worker.Sla;

namespace DisputeDesk.Worker;

/// <summary>Composes the Worker's generic host: telemetry, the database, the outbox relay and the SLA sweep.</summary>
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
        // The error names the key, never its value (Microsoft Learn, Options pattern: options validation).
        builder.Services.AddOptions<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.DisputeDesk), "ConnectionStrings:DisputeDesk is required.")
            .ValidateOnStart();
        builder.Services.AddHostedService<OutboxRelay>();
        builder.Services.AddHostedService<SlaSweep>();
        return builder;
    }
}
