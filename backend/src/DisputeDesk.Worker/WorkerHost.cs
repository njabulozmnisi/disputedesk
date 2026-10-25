using DisputeDesk.Infrastructure;

namespace DisputeDesk.Worker;

/// <summary>Composes the Worker's generic host. The outbox relay registers here in a later story.</summary>
public static class WorkerHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddInfrastructure();
        return builder;
    }
}
