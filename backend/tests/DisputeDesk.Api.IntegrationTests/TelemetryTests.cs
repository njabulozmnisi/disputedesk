using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DisputeDesk.Api.IntegrationTests;

public class TelemetryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Log_records_written_inside_a_request_carry_its_trace_and_span_ids()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                telemetry.AddTo(services);
                ThrowingHealthCheckService.Register(services);
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        var traceId = await ReadTraceIdAsync(response);
        var span = await telemetry.WaitForSpanAsync(activity => activity.Kind == ActivityKind.Server && activity.TraceId.ToHexString() == traceId);
        // Log providers belong to one host, so every record here is from this test's Api.
        var record = Assert.Single(telemetry.Logs, log => log.CategoryName == typeof(UnhandledExceptionHandler).FullName);
        Assert.Equal(span.TraceId, record.TraceId);
        Assert.Equal(span.SpanId, record.SpanId);
        // Structured: the template's placeholders arrive as attributes, not only as formatted text.
        var attributes = record.Attributes ?? [];
        Assert.Contains(new KeyValuePair<string, object?>("Method", "GET"), attributes);
        Assert.Contains(attributes, attribute => attribute is { Key: "{OriginalFormat}", Value: "Unhandled exception on {Method} {Endpoint}" });
    }

    [Fact]
    public async Task A_request_records_the_http_server_duration_metric()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: telemetry.AddTo);
        using var client = factory.CreateClient();

        await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        var metric = await telemetry.WaitForMetricAsync(factory.Services, "http.server.request.duration");
        Assert.Equal(MetricType.Histogram, metric.MetricType);
    }

    [Fact]
    public async Task Npgsql_commands_produce_client_spans()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: telemetry.AddTo);
        var marker = $"npgsql-probe-{Guid.NewGuid():N}";
        var sql = "SELECT 1 /* " + marker + " */";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);
        }

        var span = await telemetry.WaitForSpanAsync(activity => HasTagContaining(activity, marker));
        Assert.Equal("Npgsql", span.Source.Name);
        Assert.Equal(ActivityKind.Client, span.Kind);
    }

    [Fact]
    public async Task HttpClient_calls_produce_client_spans()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: telemetry.AddTo);
        _ = factory.Services; // builds the host, and with it the tracer provider
        var marker = $"http-probe-{Guid.NewGuid():N}";
        using var http = new HttpClient();

        // Nothing listens on port 1: the call fails fast but is still traced.
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            http.GetAsync(new Uri($"http://127.0.0.1:1/{marker}"), TestContext.Current.CancellationToken));

        var span = await telemetry.WaitForSpanAsync(activity => HasTagContaining(activity, marker));
        Assert.Equal("System.Net.Http", span.Source.Name);
        Assert.Equal(ActivityKind.Client, span.Kind);
    }

    [Fact]
    public async Task Every_signal_identifies_the_Api_and_its_version()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        var expectedVersion = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(expectedVersion));

        BaseProvider[] providers =
        [
            factory.Services.GetRequiredService<TracerProvider>(),
            factory.Services.GetRequiredService<MeterProvider>(),
            factory.Services.GetRequiredService<LoggerProvider>(),
        ];

        Assert.All(providers, provider =>
        {
            var attributes = provider.GetResource().Attributes.ToDictionary();
            Assert.Equal("disputedesk-api", attributes["service.name"]);
            Assert.Equal(expectedVersion, attributes["service.version"]);
        });
    }

    [Fact]
    public async Task OTEL_SERVICE_NAME_overrides_the_default_service_name()
    {
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string?> { ["OTEL_SERVICE_NAME"] = "renamed-by-environment" });

        var attributes = factory.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary();

        Assert.Equal("renamed-by-environment", attributes["service.name"]);
    }

    private static bool HasTagContaining(Activity activity, string marker) =>
        activity.TagObjects.Any(tag => tag.Value is string value && value.Contains(marker, StringComparison.Ordinal));

    private static async Task<string?> ReadTraceIdAsync(HttpResponseMessage response)
    {
        using var problem = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        return problem.RootElement.GetProperty("traceId").GetString();
    }
}
