using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace DisputeDesk.Api.Health;

/// <summary>Liveness has no dependencies; readiness checks every dependency the Api needs to serve traffic.</summary>
public static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddDisputeDeskHealthChecks(this IServiceCollection services)
    {
        // Kafka (Redpanda) and Zitadel readiness checks arrive with the stories that introduce them.
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("postgres", tags: [ReadyTag]);
        return services;
    }

    public static IEndpointRouteBuilder MapDisputeDeskHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
        return app;
    }
}
