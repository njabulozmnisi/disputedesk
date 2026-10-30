using System.Diagnostics;
using DisputeDesk.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// A Worker host built the way Program builds it, on a test clock, with its own in-memory exporters. Activity and
/// meter listeners are process-wide, so spans and metrics are picked by message ID; logs come from this host's own
/// logger factory only.
/// </summary>
public sealed class RelayHost : IAsyncDisposable
{
    private static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(10);

    private IHost? _host;

    public LockedCollection<Activity> Spans { get; } = new();

    public LockedCollection<LogRecord> Logs { get; } = new();

    public LockedCollection<Metric> Metrics { get; } = new();

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Not started.");

    /// <summary>The producer spans the process exported on the DisputeDesk ActivitySource.</summary>
    public IReadOnlyList<Activity> ProducerSpans =>
        [.. Spans.Snapshot().Where(span => span.Source.Name == Telemetry.ActivitySourceName && span.Kind == ActivityKind.Producer)];

    /// <summary>This host's log lines as written.</summary>
    public IReadOnlyList<string> LogLines => [.. Logs.Snapshot().Select(record => record.FormattedMessage ?? record.Body ?? "")];

    public static async Task<RelayHost> StartAsync(TimeProvider clock, string connectionString, string bootstrapServers)
    {
        var relayHost = new RelayHost();
        HostApplicationBuilder builder = WorkerHost.CreateBuilder(
        [
            "--environment", "Development",
            $"--ConnectionStrings:DisputeDesk={connectionString}",
            $"--Kafka:BootstrapServers={bootstrapServers}",
        ]);
        builder.Services.AddSingleton(clock);
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(relayHost.Spans));
        builder.Services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(relayHost.Logs));
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(relayHost.Metrics));
        relayHost._host = builder.Build();
        await relayHost._host.StartAsync(TestContext.Current.CancellationToken);
        return relayHost;
    }

    /// <summary>The spans exported for one outbox row, by <c>messaging.message.id</c>.</summary>
    public IReadOnlyList<Activity> ProducerSpansFor(Guid messageId) =>
        [.. ProducerSpans.Where(span => span.GetTagItem("messaging.message.id") as string == messageId.ToString())];

    public async ValueTask DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        using var stopTimeout = new CancellationTokenSource(StopBudget);
        await _host.StopAsync(stopTimeout.Token);
        _host.Dispose();
    }
}
