using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// Listens to <c>disputedesk.linking.events</c> of one test Api only (its own meter factory is the meter's scope) and
/// counts the measurements by their <c>event</c> label.
/// </summary>
public sealed class LinkingEvents : IDisposable
{
    private readonly ConcurrentQueue<string> _events = new();
    private readonly MeterListener _listener;

    public LinkingEvents(ApiFactory factory)
    {
        IMeterFactory meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Name == "disputedesk.linking.events")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        // Every measurement adds 1, so counting measurements counts events.
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "event")
                {
                    _events.Enqueue(tag.Value?.ToString() ?? "");
                }
            }
        });
        _listener.Start();
    }

    public int Count(string linkingEvent) => _events.Count(recorded => recorded == linkingEvent);

    public void Dispose() => _listener.Dispose();
}
