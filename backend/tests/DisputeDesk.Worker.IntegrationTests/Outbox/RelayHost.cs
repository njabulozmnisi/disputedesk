using System.Diagnostics;
using Confluent.Kafka;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// A Worker host built the way Program builds it, on a test clock, with its own in-memory exporters. Activity and
/// meter listeners are process-wide, so spans and metrics are picked by message ID; logs come from this host's own
/// logger factory only. It stops with the default 30-second <c>HostOptions.ShutdownTimeout</c>, as the Worker does (AD-25).
/// </summary>
public sealed class RelayHost : IAsyncDisposable
{
    private IHost? _host;
    private ActivityListener? _sendListener;
    private bool _stopped;

    public LockedCollection<Activity> Spans { get; } = new();

    public LockedCollection<LogRecord> Logs { get; } = new();

    public LockedCollection<Metric> Metrics { get; } = new();

    /// <summary>
    /// Every producer span started in the process, ended or not (System.Diagnostics <c>ActivityListener</c>). The span starts
    /// just before <c>ProduceAsync</c>, so a started span that has not ended is a send in flight.
    /// </summary>
    public LockedCollection<Activity> StartedSpans { get; } = new();

    /// <summary>The relay's <c>BackgroundService.ExecuteTask</c>: still running unless the relay stopped or threw.</summary>
    public Task? RelayTask => Services.GetServices<IHostedService>().OfType<OutboxRelay>().Single().ExecuteTask;

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Not started.");

    /// <summary>The producer spans the process exported on the DisputeDesk ActivitySource.</summary>
    public IReadOnlyList<Activity> ProducerSpans =>
        [.. Spans.Snapshot().Where(span => span.Source.Name == Telemetry.ActivitySourceName && span.Kind == ActivityKind.Producer)];

    /// <summary>This host's log lines as written.</summary>
    public IReadOnlyList<string> LogLines => [.. Logs.Snapshot().Select(record => record.FormattedMessage ?? record.Body ?? "")];

    /// <summary>
    /// Starts the host on <paramref name="clock"/>. A <paramref name="buildProducer"/>, when given, is registered for the
    /// relay's constructor and builds its producer from the relay's configured builder.
    /// </summary>
    public static async Task<RelayHost> StartAsync(
        TimeProvider clock,
        string connectionString,
        string bootstrapServers,
        Func<ProducerBuilder<string, string>, IProducer<string, string>>? buildProducer = null)
    {
        var relayHost = new RelayHost();
        HostApplicationBuilder builder = WorkerHost.CreateBuilder(
        [
            "--environment", "Development",
            $"--ConnectionStrings:DisputeDesk={connectionString}",
            $"--Kafka:BootstrapServers={bootstrapServers}",
        ]);
        builder.Services.AddSingleton(clock);
        if (buildProducer is not null)
        {
            builder.Services.AddSingleton(buildProducer);
        }

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(relayHost.Spans));
        builder.Services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(relayHost.Logs));
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(relayHost.Metrics));
        relayHost._sendListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.ActivitySourceName,
            // Leaves the sampling decision to the OpenTelemetry SDK; this listener only watches.
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
            ActivityStarted = relayHost.StartedSpans.Add,
        };
        ActivitySource.AddActivityListener(relayHost._sendListener);
        relayHost._host = builder.Build();
        await relayHost._host.StartAsync(TestContext.Current.CancellationToken);
        return relayHost;
    }

    /// <summary>The spans exported for one outbox row, by <c>messaging.message.id</c>.</summary>
    public IReadOnlyList<Activity> ProducerSpansFor(Guid messageId) =>
        [.. ProducerSpans.Where(span => span.GetTagItem("messaging.message.id") as string == messageId.ToString())];

    /// <summary>True once a producer span for <paramref name="messageId"/> has started: that row's send is in flight or done.</summary>
    public bool SendStarted(Guid messageId) =>
        StartedSpans.Snapshot().Any(span => span.GetTagItem("messaging.message.id") as string == messageId.ToString());

    /// <summary>
    /// Stops the host as SIGTERM does (the console lifetime calls <c>StopAsync</c>), bounded only by the host's own
    /// <c>ShutdownTimeout</c>, and returns how long it took.
    /// </summary>
    public async Task<TimeSpan> StopAsync()
    {
        IHost host = _host ?? throw new InvalidOperationException("Not started.");
        _stopped = true;
        var elapsed = Stopwatch.StartNew();
        await host.StopAsync(CancellationToken.None);
        return elapsed.Elapsed;
    }

    public async ValueTask DisposeAsync()
    {
        _sendListener?.Dispose();
        if (_host is null)
        {
            return;
        }

        if (!_stopped)
        {
            await StopAsync();
        }

        _host.Dispose();
    }
}
