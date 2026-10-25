using DisputeDesk.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// Proves the on/off decision our code owns: OTEL_EXPORTER_OTLP_ENDPOINT alone switches the OTLP exporter on.
/// What the exporter sends once switched on is the OpenTelemetry library's behaviour, not ours.
/// </summary>
public class OtlpExportTests
{
    [Theory]
    [InlineData("", false)] // blank, not merely absent, so a developer's own variable can't leak into the run
    [InlineData("http://127.0.0.1:4317", true)]
    public void The_OTLP_exporter_is_registered_only_when_an_endpoint_is_set(string endpoint, bool expected)
    {
        ServiceDescriptor[] registrations = [];
        using var factory = new ApiFactory(
            connectionString: null,
            configureServices: services => registrations = [.. services],
            settings: new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint });

        _ = factory.Services; // builds the host, which runs Program and then the callback above

        Assert.Equal(expected, registrations.Any(FromOtlpExporter));
    }

    /// <summary>A service, implementation or options type from the OTLP exporter's own assembly.</summary>
    public static bool FromOtlpExporter(ServiceDescriptor descriptor)
    {
        var otlp = typeof(OtlpExporterOptions).Assembly;
        Type?[] types = [descriptor.ServiceType, descriptor.ImplementationType, .. descriptor.ServiceType.GenericTypeArguments];
        return types.Any(type => type?.Assembly == otlp);
    }
}
