using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Adds in-memory exporters to the Api's own OpenTelemetry pipeline, alongside whatever it already exports.
/// Activity and meter listeners are process-wide, so spans and metrics from other tests running in parallel
/// can land here too: assertions must pick their own span by a unique path or trace ID.
/// </summary>
public sealed class TelemetryCapture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly LockedList<Activity> _spans = new();
    private readonly LockedList<LogRecord> _logs = new();
    private readonly LockedList<Metric> _metrics = new();

    public void AddTo(IServiceCollection services)
    {
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(_spans));
        services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(_logs));
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(_metrics));
    }

    public IReadOnlyList<LogRecord> Logs => _logs.Snapshot();

    /// <summary>The spans exported so far for one trace.</summary>
    public IReadOnlyList<Activity> Spans(ActivityTraceId traceId) => [.. _spans.Snapshot().Where(span => span.TraceId == traceId)];

    /// <summary>
    /// The server span is exported when the request activity stops, which can be just after the client
    /// has the response, so wait for it.
    /// </summary>
    public Task<Activity> WaitForSpanAsync(Func<Activity, bool> match) =>
        WaitForAsync(() => _spans.Snapshot().FirstOrDefault(match));

    public Task<Metric> WaitForMetricAsync(IServiceProvider services, string name) =>
        WaitForAsync(() =>
        {
            services.GetRequiredService<MeterProvider>().ForceFlush();
            return _metrics.Snapshot().FirstOrDefault(metric => metric.Name == name);
        });

    private static async Task<T> WaitForAsync<T>(Func<T?> probe)
        where T : class
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < Timeout)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No matching {typeof(T).Name} was exported within {Timeout}.");
    }

    /// <summary>The exporters add from whichever thread ends the span or log, so writes and reads take a lock.</summary>
    private sealed class LockedList<T> : ICollection<T>
    {
        private readonly List<T> _items = [];
        private readonly Lock _gate = new();

        public int Count { get { lock (_gate) { return _items.Count; } } }

        public bool IsReadOnly => false;

        public IReadOnlyList<T> Snapshot() { lock (_gate) { return [.. _items]; } }

        public void Add(T item) { lock (_gate) { _items.Add(item); } }

        public void Clear() { lock (_gate) { _items.Clear(); } }

        public bool Contains(T item) { lock (_gate) { return _items.Contains(item); } }

        public void CopyTo(T[] array, int arrayIndex) { lock (_gate) { _items.CopyTo(array, arrayIndex); } }

        public bool Remove(T item) { lock (_gate) { return _items.Remove(item); } }

        public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
