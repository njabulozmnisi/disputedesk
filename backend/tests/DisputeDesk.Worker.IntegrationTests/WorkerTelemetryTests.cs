using System.Diagnostics;
using System.Reflection;
using DisputeDesk.Worker.Sla;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DisputeDesk.Worker.IntegrationTests;

public class WorkerTelemetryTests
{
    [Fact]
    public void AddDisputeDeskTelemetry_InWorker_TagsEverySignalWithServiceAndVersion()
    {
        using IHost host = WorkerHost.CreateBuilder(["--environment", "Development"]).Build();
        string? expectedVersion = typeof(WorkerHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(expectedVersion));

        BaseProvider[] providers =
        [
            host.Services.GetRequiredService<TracerProvider>(),
            host.Services.GetRequiredService<MeterProvider>(),
            host.Services.GetRequiredService<LoggerProvider>(),
        ];

        Assert.All(providers, provider =>
        {
            var attributes = provider.GetResource().Attributes.ToDictionary();
            Assert.Equal("disputedesk-worker", attributes["service.name"]);
            Assert.Equal(expectedVersion, attributes["service.version"]);
        });
    }

    [Fact]
    public async Task HttpClient_CallFromWorker_ProducesClientSpan()
    {
        var spans = new List<Activity>();
        HostApplicationBuilder builder = WorkerHost.CreateBuilder(["--environment", "Development"]);
        // No SLA sweep: a started host would escalate the due disputes of whatever database its configuration names.
        builder.Services.Remove(builder.Services.Single(service => service.ImplementationType == typeof(SlaSweep)));
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(spans));
        using IHost host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        string marker = $"worker-http-probe-{Guid.NewGuid():N}";
        using var http = new HttpClient();

        // Nothing listens on port 1: the call fails fast but is still traced. The span ends before the throw.
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            http.GetAsync(new Uri($"http://127.0.0.1:1/{marker}"), TestContext.Current.CancellationToken));
        await host.StopAsync(TestContext.Current.CancellationToken);

        Activity span = Assert.Single(spans, activity => activity.TagObjects.Any(tag => tag.Value is string value && value.Contains(marker, StringComparison.Ordinal)));
        Assert.Equal(ActivityKind.Client, span.Kind);
    }

    [Theory]
    [InlineData("", false)] // blank, not merely absent: the command line overrides a developer's own variable
    [InlineData("http://127.0.0.1:4317", true)]
    public void AddDisputeDeskTelemetry_OtlpEndpoint_RegistersExporterOnlyWhenSet(string endpoint, bool expected)
    {
        HostApplicationBuilder builder = WorkerHost.CreateBuilder(["--environment", "Development", $"--OTEL_EXPORTER_OTLP_ENDPOINT={endpoint}"]);

        Assert.Equal(expected, builder.Services.Any(FromOtlpExporter));
    }

    /// <summary>A service, implementation or options type from the OTLP exporter's own assembly.</summary>
    private static bool FromOtlpExporter(ServiceDescriptor descriptor)
    {
        Assembly otlp = typeof(OtlpExporterOptions).Assembly;
        Type?[] types = [descriptor.ServiceType, descriptor.ImplementationType, .. descriptor.ServiceType.GenericTypeArguments];
        return types.Any(type => type?.Assembly == otlp);
    }
}
