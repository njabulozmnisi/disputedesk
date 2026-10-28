using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC11: <c>disputedesk.disputes.raised</c> by <c>reason</c> and <c>disputedesk.raise.rejections</c> by <c>status</c>.
/// A <see cref="MeterListener"/> listens only to meters of this test's own Api (its <see cref="IMeterFactory"/>), so
/// raises in tests running alongside are never counted here.
/// </summary>
public class RaiseMetricsTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Raise_SucceedsThenRefused409And422_CountsRaisedByReasonAndRejectionsByStatus()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        await browser.RaiseAsync(DisputesApi.Body(reason: "duplicateCharge"));
        await browser.RaiseAsync(DisputesApi.Body());                         // 409, already disputed
        await browser.RaiseAsync(DisputesApi.Body(DisputesApi.Pending));      // 422, not disputable
        await browser.RaiseAsync(DisputesApi.Body("TXN-A-9999"));             // 404, not a rejection

        Assert.Equal([("reason", "duplicateCharge")], recorder.Tags("disputedesk.disputes.raised"));
        Assert.Equal([("status", "409"), ("status", "422")], recorder.Tags("disputedesk.raise.rejections").Order());
    }

    [Fact]
    public async Task Raise_AnyOutcome_LabelsCarryNoPersonalData()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        await browser.RaiseAsync(DisputesApi.Body(note: "note-marker"));
        await browser.RaiseAsync(DisputesApi.Body());

        Assert.All(recorder.AllTagNames(), name => Assert.True(name is "reason" or "status", name));
    }

    /// <summary>Every long measurement of the DisputeDesk meter created by one <see cref="IMeterFactory"/>.</summary>
    private sealed class MeasurementRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, long Value, (string Name, string Value)[] Tags)> _measurements = new();

        public MeasurementRecorder(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name == "DisputeDesk")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => _measurements.Enqueue(
                (instrument.Name, value, [.. tags.ToArray().Select(tag => (tag.Key, Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""))])));
            _listener.Start();
        }

        /// <summary>The tags of each measurement of <paramref name="instrument"/>, one per counted raise.</summary>
        public List<(string Name, string Value)> Tags(string instrument) =>
            [.. _measurements.Where(measurement => measurement.Instrument == instrument)
                .SelectMany(measurement => Enumerable.Repeat(Assert.Single(measurement.Tags), (int)measurement.Value))];

        public IEnumerable<string> AllTagNames() => _measurements.SelectMany(measurement => measurement.Tags.Select(tag => tag.Name));

        public void Dispose() => _listener.Dispose();
    }
}
