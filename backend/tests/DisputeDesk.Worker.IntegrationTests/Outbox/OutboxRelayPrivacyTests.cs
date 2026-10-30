using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// No personal data crosses the Kafka boundary or the relay's telemetry (NFR-SEC-5; AD-20; OWASP Logging Cheat Sheet,
/// data to exclude; ASVS 5.0 V16.2): story 4.1's criterion 7 on a publish, story 4.2's criterion 6 on the failure paths.
/// Every private field of <see cref="DisputeRaised"/> and the actor ID carry a marker (<see cref="OutboxFixture.CommitMarkerDisputeAsync"/>).
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayPrivacyTests(OutboxFixture outbox)
{
    [Fact]
    public async Task Relay_PublishesARaise_NoLogSpanMetricTagOrHeaderHoldsAPrivateField()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        DateTimeOffset now = clock.GetUtcNow();
        OutboxMessage row = await outbox.CommitMarkerDisputeAsync(now);

        var observed = new List<string>();
        await using (RelayHost host = await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([row.Id]);
            await OutboxFixture.WaitUntilAsync(() => host.ProducerSpansFor(row.Id).Count == 1);
            host.Services.GetRequiredService<MeterProvider>().ForceFlush();
            observed.AddRange(TelemetryValues(host));
        }

        ConsumeResult<string, string> record = Assert.Single(outbox.RecordsFor(row.DisputeId));
        observed.AddRange(KeyAndHeaders(record));

        Assert.NotEmpty(observed);
        Assert.All(OutboxFixture.Markers(now), marker => Assert.DoesNotContain(observed, value => value.Contains(marker, StringComparison.Ordinal)));
        // The record value is the committed payload, whose content 3.8's tests guard (PublishMappingTests, RaisedEventTests).
        Assert.Equal(row.Payload, record.Message.Value);
    }

    [Fact]
    public async Task Relay_SendFailsInABrokerPauseThenARowIsRefusedUntilFailed_NoFailureSurfaceHoldsAPrivateField()
    {
        RelayClock clock = OutboxFixture.NewClock();
        // A day back, so the marker row is the oldest due row and the one whose send the pause holds.
        DateTimeOffset at = clock.GetUtcNow().AddDays(-1);
        OutboxMessage raised = await outbox.CommitMarkerDisputeAsync(at);
        OutboxMessage refused = await outbox.AddOversizedVersionAsync(raised.DisputeId, 2, at, OutboxFixture.MarkerText);

        var observed = new List<string>();
        IReadOnlyList<LogRecord> logs;
        IReadOnlyList<Activity> failedSpans;
        await outbox.PauseBrokerAsync();
        bool paused = true;
        try
        {
            await using RelayHost host = await outbox.StartRelayAsync(clock);
            // The marker row's send times out while the broker is paused (10 s); the relay then waits out its backoff.
            await OutboxFixture.WaitUntilAsync(() => host.ProducerSpansFor(raised.Id).Any(span => span.Status == ActivityStatusCode.Error));
            await outbox.UnpauseBrokerAsync();
            paused = false;
            // Each step ends one backoff; the producer may need more than one try after the pause.
            await OutboxFixture.WaitUntilAsync(async () =>
            {
                if ((await outbox.RowAsync(raised.Id)).Status == OutboxStatus.Sent)
                {
                    return true;
                }

                await clock.StepAsync(TimeSpan.FromSeconds(10));
                return false;
            });
            List<(DateTimeOffset RefusedAt, OutboxMessage Row)> refusals = await outbox.RefuseFiveTimesAsync(clock, refused.Id);
            Assert.Equal(OutboxStatus.Failed, refusals[^1].Row.Status);
            observed.AddRange(refusals.Select(refusal => refusal.Row.LastError ?? ""));

            host.Services.GetRequiredService<MeterProvider>().ForceFlush();
            observed.AddRange(TelemetryValues(host));
            logs = host.Logs.Snapshot();
            failedSpans = [.. host.Spans.Snapshot().Where(span => span.Status == ActivityStatusCode.Error)];
        }
        finally
        {
            if (paused)
            {
                await outbox.UnpauseBrokerAsync();
            }
        }

        observed.AddRange(outbox.RecordsFor(raised.DisputeId).SelectMany(KeyAndHeaders));

        Assert.Contains(failedSpans, span => span.GetTagItem("messaging.message.id") as string == raised.Id.ToString());
        Assert.Contains(failedSpans, span => span.GetTagItem("messaging.message.id") as string == refused.Id.ToString());
        Assert.All(OutboxFixture.Markers(at), marker => Assert.DoesNotContain(observed, value => value.Contains(marker, StringComparison.Ordinal)));
        // Failure-path logs take no exception argument, and a failed span has no description and no exception event.
        Assert.All(logs, record => Assert.Null(record.Exception));
        Assert.All(failedSpans, span =>
        {
            Assert.Null(span.StatusDescription);
            Assert.Empty(span.Events);
        });
    }

    [Fact]
    public async Task Relay_FatalProducerError_RecreatesTheProducerAndNoFailureSurfaceHoldsAPrivateField()
    {
        RelayClock clock = OutboxFixture.NewClock();
        // A day back, so the marker row is the oldest due row and the one the failing producer gets.
        DateTimeOffset at = clock.GetUtcNow().AddDays(-1);
        OutboxMessage raised = await outbox.CommitMarkerDisputeAsync(at);
        // Confluent.Kafka puts librdkafka's own text in Error.Reason; here that text holds the markers.
        var fatal = new Error(ErrorCode.OutOfOrderSequenceNumber, OutboxFixture.MarkerText, isFatal: true);
        // The first producer fails its first send with the fatal error; every later one is the real producer.
        var built = new LockedCollection<IProducer<string, string>>();
        IProducer<string, string> BuildProducer(ProducerBuilder<string, string> builder)
        {
            IProducer<string, string> producer = built.Count == 0 ? new FailingProducer(fatal, 1, builder.Build()) : builder.Build();
            built.Add(producer);
            return producer;
        }

        var observed = new List<string>();
        IReadOnlyList<LogRecord> logs;
        IReadOnlyList<Activity> failedSpans;
        await using (RelayHost host = await outbox.StartRelayAsync(clock, BuildProducer))
        {
            // The fatal send: the relay logs the recreate line, builds a new producer and waits out its backoff.
            await OutboxFixture.WaitUntilAsync(() => clock.RelayWaitsNow && RecreateLines(host).Count == 1);
            OutboxMessage during = await outbox.RowAsync(raised.Id);
            Assert.Equal((OutboxStatus.Pending, 0, (string?)null), (during.Status, during.Attempts, during.LastError));

            await outbox.WaitUntilSentAsync([raised.Id], clock);
            host.Services.GetRequiredService<MeterProvider>().ForceFlush();
            observed.AddRange(TelemetryValues(host));
            logs = host.Logs.Snapshot();
            failedSpans = [.. host.ProducerSpansFor(raised.Id).Where(span => span.Status == ActivityStatusCode.Error)];

            LogRecord recreate = Assert.Single(RecreateLines(host));
            Assert.Equal(LogLevel.Error, recreate.LogLevel);
            Assert.Equal(
                new Dictionary<string, string?> { ["ErrorCode"] = "OutOfOrderSequenceNumber" },
                (recreate.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").ToDictionary(attribute => attribute.Key, attribute => attribute.Value?.ToString()));
        }

        // The failing producer was disposed and one new producer built, which sent the row with no attempt counted.
        Assert.Equal(2, built.Count);
        Assert.True(Assert.IsType<FailingProducer>(built.Snapshot()[0]).Disposed);
        OutboxMessage after = await outbox.RowAsync(raised.Id);
        Assert.Equal((OutboxStatus.Sent, 0, (string?)null), (after.Status, after.Attempts, after.LastError));
        ConsumeResult<string, string> record = Assert.Single(outbox.RecordsFor(raised.DisputeId));
        observed.AddRange(KeyAndHeaders(record));

        Activity failedSpan = Assert.Single(failedSpans);
        Assert.Equal("OutOfOrderSequenceNumber", failedSpan.GetTagItem("error.type"));
        Assert.All(OutboxFixture.Markers(at), marker => Assert.DoesNotContain(observed, value => value.Contains(marker, StringComparison.Ordinal)));
        // Failure-path logs take no exception argument, and the failed span has no description and no exception event.
        Assert.All(logs, line => Assert.Null(line.Exception));
        Assert.Null(failedSpan.StatusDescription);
        Assert.Empty(failedSpan.Events);
    }

    private static IReadOnlyList<LogRecord> RecreateLines(RelayHost host) =>
        [.. host.Logs.Snapshot().Where(record => record.FormattedMessage?.StartsWith("Outbox relay's Kafka producer hit a fatal error", StringComparison.Ordinal) == true)];

    /// <summary>Every log line's message, body and attribute values, span name, tag, status description and event, and metric tag.</summary>
    private static List<string> TelemetryValues(RelayHost host)
    {
        var values = new List<string>();
        values.AddRange(host.Logs.Snapshot().SelectMany(record =>
            new[] { record.FormattedMessage, record.Body, record.Exception?.ToString() }
                .Concat((record.Attributes ?? []).Select(attribute => attribute.Value?.ToString()))
                .OfType<string>()));
        values.AddRange(host.Spans.Snapshot().SelectMany(span =>
            new[] { span.DisplayName, span.StatusDescription }
                .Concat(span.TagObjects.Select(tag => tag.Value?.ToString()))
                .Concat(span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString()).Prepend(spanEvent.Name)))
                .OfType<string>()));
        values.AddRange(host.Metrics.Snapshot().SelectMany(MetricTagValues));
        return values;
    }

    private static IEnumerable<string> KeyAndHeaders(ConsumeResult<string, string> record) =>
        record.Message.Headers.Select(header => header.Key + "=" + Encoding.UTF8.GetString(header.GetValueBytes())).Prepend(record.Message.Key);

    private static List<string> MetricTagValues(Metric metric)
    {
        var values = new List<string>();
        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            foreach (KeyValuePair<string, object?> tag in point.Tags)
            {
                values.Add($"{tag.Key}={tag.Value}");
            }
        }

        return values;
    }
}
