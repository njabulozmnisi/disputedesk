using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>Makes /health/live throw, to drive the unhandled-exception path through the real pipeline.</summary>
public sealed class ThrowingHealthCheckService : HealthCheckService
{
    public const string SecretDetail = "secret-detail-that-must-not-leak";

    public static void Register(IServiceCollection services) =>
        services.AddSingleton<HealthCheckService, ThrowingHealthCheckService>();

    public override Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(SecretDetail);
}
