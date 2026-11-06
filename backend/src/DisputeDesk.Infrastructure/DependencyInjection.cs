using DisputeDesk.Application.Abstractions;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DisputeDesk.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DisputeDesk";

    /// <summary>Registers the clock and the database for both hosts (Api and Worker).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // The connection string is read when the context is first built, not at registration, so
        // build-time OpenAPI generation (which runs Program without configuration) never needs it.
        // A missing one fails on first connect rather than on construction, so /health/ready
        // reports it as Unhealthy (503) instead of an unhandled 500.
        services.AddDbContext<AppDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        return services;
    }

    /// <summary>
    /// Registers the outbox relay and the SLA sweep, which the Worker always runs and the Api runs when
    /// <c>Worker:InProcess</c> is true (AD-27). Both need <c>ConnectionStrings:DisputeDesk</c>, and the relay
    /// <c>Kafka:BootstrapServers</c>; the error names the key, never its value (Microsoft Learn, Options pattern:
    /// options validation).
    /// </summary>
    public static IServiceCollection AddOutboxRelayAndSlaSweep(this IServiceCollection services)
    {
        services.AddOptions<KafkaOptions>()
            .BindConfiguration(KafkaOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "Kafka:BootstrapServers is required.")
            .ValidateOnStart();
        services.AddOptions<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.DisputeDesk), $"{ConnectionStringsOptions.SectionName}:{ConnectionStringName} is required.")
            .ValidateOnStart();
        services.AddHostedService<OutboxRelay>();
        services.AddHostedService<SlaSweep>();
        return services;
    }
}
