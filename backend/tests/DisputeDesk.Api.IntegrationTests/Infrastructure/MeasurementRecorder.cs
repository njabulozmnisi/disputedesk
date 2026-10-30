using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Every measurement of the DisputeDesk meter created by one <see cref="IMeterFactory"/>, long and double, so a test
/// listens only to its own Api's meters (Microsoft Learn, "Collect metrics": MeterListener).
/// </summary>
public sealed class MeasurementRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, Instrument> _instruments = new();
    private readonly ConcurrentQueue<(string Instrument, double Value, (string Name, string Value)[] Tags)> _measurements = new();

    public MeasurementRecorder(IMeterFactory meterFactory)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name == "DisputeDesk")
            {
                _instruments[instrument.Name] = instrument;
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public Instrument? Instrument(string name) => _instruments.GetValueOrDefault(name);

    /// <summary>The measurements of <paramref name="instrument"/> in order, each with its tags sorted by name.</summary>
    public List<(double Value, (string Name, string Value)[] Tags)> Measurements(string instrument) =>
        [.. _measurements.Where(measurement => measurement.Instrument == instrument).Select(measurement => (measurement.Value, measurement.Tags))];

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        _measurements.Enqueue((instrument.Name, value,
            [.. tags.ToArray().Select(tag => (tag.Key, Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? "")).Order()]));
}
