using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AD-22, NFR-SEC-5: a stored event holds the customer ID, the account reference, the transaction snapshot, the note and
/// the actor's login, and none of them reaches a log line, a span attribute or a metric label while a raise commits.
/// <c>EnableSensitiveDataLogging</c> stays off outside tests (<see cref="DatabaseLoggingTests"/>).
/// </summary>
public class StoredEventPrivacyTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string LoginSub = "login-a";
    private const string Note = "privacy-note-marker";

    // Customer A's disputable transaction as the fixtures hold it: its ID, masked card and description.
    private static readonly string[] Private =
        [TransactionsApi.CustomerA, DisputesApi.AccountA, DisputesApi.Disputable, "492181******4417", "Steers Braamfontein", Note, LoginSub];

    [Fact]
    public async Task Raise_Commits_NoLogSpanOrMetricLabelHoldsAnyStoredEventData()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        using var labels = new LabelRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient browser = await factory.SignInLinkedAsync(LoginSub, TransactionsApi.CustomerA);
        // A trace ID of the test's own, so spans from tests running in parallel are never mistaken for this request's.
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(DisputesApi.Body(note: Note), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", DisputesApi.NewKey());
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The server span ends last, so once it is exported the database spans of the commit are too.
        await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
        IReadOnlyList<Activity> spans = telemetry.Spans(traceId);
        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        Assert.All(spans, span => AssertNothingPrivate(SpanTexts(span)));
        Assert.NotEmpty(telemetry.Logs);
        Assert.All(telemetry.Logs, log => AssertNothingPrivate(LogTexts(log)));
        Assert.NotEmpty(labels.Values);
        AssertNothingPrivate(labels.Values);
    }

    private static IEnumerable<string?> SpanTexts(Activity span) =>
    [
        span.DisplayName,
        span.StatusDescription,
        .. span.TagObjects.Select(tag => tag.Value?.ToString()),
        .. span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString())),
    ];

    private static IEnumerable<string?> LogTexts(LogRecord log) =>
    [
        log.FormattedMessage,
        log.Body,
        log.Exception?.ToString(),
        .. (log.Attributes ?? []).Select(attribute => attribute.Value?.ToString()),
    ];

    private static void AssertNothingPrivate(IEnumerable<string?> texts)
    {
        foreach (string text in texts.OfType<string>())
        {
            Assert.All(Private, value => Assert.DoesNotContain(value, text, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Every label value of every instrument of one Api's meters. A <see cref="MeterListener"/> listens only to meters
    /// of that Api's <see cref="IMeterFactory"/>, so measurements of tests running alongside are never recorded here.
    /// </summary>
    private sealed class LabelRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<string?> _values = new();

        public LabelRecorder(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) => Record(tags));
            _listener.SetMeasurementEventCallback<double>((_, _, tags, _) => Record(tags));
            _listener.Start();
        }

        public IReadOnlyList<string?> Values => [.. _values];

        public void Dispose() => _listener.Dispose();

        private void Record(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                _values.Enqueue(tag.Value?.ToString());
            }
        }
    }
}
