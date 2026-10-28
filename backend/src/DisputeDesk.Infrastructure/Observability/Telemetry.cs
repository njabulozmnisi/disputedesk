using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DisputeDesk.Infrastructure.Observability;

/// <summary>OpenTelemetry traces, metrics and logs shared by both hosts (Api and Worker).</summary>
public static class Telemetry
{
    /// <summary>The name of DisputeDesk's own Meter (AD-20), whose business metrics both hosts export.</summary>
    public const string MeterName = "DisputeDesk";

    /// <summary>
    /// Registers the three signals with HttpClient and Npgsql instrumentation. Export is configured only by the
    /// standard OTEL_* variables: OTLP is switched on when OTEL_EXPORTER_OTLP_ENDPOINT is set, and with it unset
    /// the host runs normally and exports nothing. Only that variable switches export on: a signal-specific
    /// endpoint alone (such as OTEL_EXPORTER_OTLP_TRACES_ENDPOINT) does not, the same rule as Aspire's service
    /// defaults. Once on, the exporter reads the rest of the OTEL_EXPORTER_OTLP_* variables itself.
    /// </summary>
    /// <param name="builder">The host being composed.</param>
    /// <param name="serviceName">The default service.name; OTEL_SERVICE_NAME overrides it.</param>
    /// <param name="hostAssembly">The host's entry assembly, whose informational version becomes service.version.</param>
    public static OpenTelemetryBuilder AddDisputeDeskTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName,
        Assembly hostAssembly)
    {
        OpenTelemetryBuilder telemetry = builder.Services.AddOpenTelemetry()
            // A later resource wins a collision, so the environment detector runs again after AddService:
            // OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES keep the last word over the code's defaults.
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: hostAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
                .AddEnvironmentVariableDetector())
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddHttpClientInstrumentation()
                .AddNpgsqlInstrumentation()
                // The runtime's built-in meter (.NET 9+): GC, thread pool, exceptions.
                .AddMeter("System.Runtime")
                .AddMeter(MeterName))
            // The log record carries the current Activity's trace and span IDs; the formatted message is kept
            // alongside the template and its attributes so a viewer can show the line as written.
            .WithLogging(configureBuilder: null, configureOptions: options => options.IncludeFormattedMessage = true);

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        return telemetry;
    }
}
