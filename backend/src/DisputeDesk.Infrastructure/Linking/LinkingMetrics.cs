using System.Diagnostics.Metrics;
using DisputeDesk.Infrastructure.Observability;

namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// <c>disputedesk.linking.events</c> (AD-20, NFR-OBS-3), created through <see cref="IMeterFactory"/> (Microsoft Learn:
/// "Creating metrics"). Its only label is the event, never personal data.
/// </summary>
public sealed class LinkingMetrics
{
    private readonly Counter<long> _events;

    public LinkingMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(Telemetry.MeterName);
        _events = meter.CreateCounter<long>("disputedesk.linking.events", description: "Linking events, by event.");
    }

    /// <summary>Counts a well-formed start the Api saved, answered 202 or, on a full queue, 429.</summary>
    public void RecordStarted() => _events.Add(1, new KeyValuePair<string, object?>("event", "started"));

    /// <summary>Counts a job stopped by the hourly send cap.</summary>
    public void RecordCapped() => _events.Add(1, new KeyValuePair<string, object?>("event", "capped"));
}
