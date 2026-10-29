using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace DisputeDesk.Api.Health;

/// <summary>Liveness has no dependencies; readiness checks every dependency the Api needs to serve traffic.</summary>
public static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddDisputeDeskHealthChecks(this IServiceCollection services)
    {
        services.AddSingleton<DatabaseMigrator>();
        // The build writing openapi.json must never migrate a database (BuildTimeOpenApi).
        if (!BuildTimeOpenApi.IsRunning)
        {
            services.AddHostedService(provider => provider.GetRequiredService<DatabaseMigrator>());
        }

        // Core banking is left out on purpose (AD-25): its outage is a 503 on the calls that need it, not "not ready".
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("postgres", tags: [ReadyTag])
            .AddCheck<DatabaseMigrator>("migrations", tags: [ReadyTag])
            .AddCheck<ZitadelDiscoveryCheck>("zitadel-discovery", tags: [ReadyTag]);
        return services;
    }

    public static IEndpointRouteBuilder MapDisputeDeskHealthChecks(this IEndpointRouteBuilder app)
    {
        // Probes carry no session, so both opt out of the fallback policy (AD-10).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous();
        return app;
    }
}
