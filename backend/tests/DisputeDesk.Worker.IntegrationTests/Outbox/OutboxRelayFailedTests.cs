using System.Diagnostics;
using Confluent.Kafka;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 3 of story 4.2: a record the client permanently refuses is retried with exponential back-off and jitter
/// (Azure Architecture Center, transient fault handling) and set Failed on the 5th refusal, which holds back the rest of
/// its dispute for an operator (AD-17, Failures).
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayFailedTests(OutboxFixture outbox)
{
    [Fact]
    public async Task Relay_RecordRefusedFiveTimes_RetriesWithJitteredBackoffThenSetsFailedAndHoldsTheDispute()
    {
        RelayClock clock = OutboxFixture.NewClock();
        DateTimeOffset now = clock.GetUtcNow();
        OutboxMessage raised = await outbox.CommitMarkerDisputeAsync(now);
        OutboxMessage refused = await outbox.AddOversizedVersionAsync(raised.DisputeId, 2, now, OutboxFixture.MarkerText);
        OutboxMessage later = await outbox.AddVersionAsync(raised.DisputeId, 3, now);

        List<(DateTimeOffset RefusedAt, OutboxMessage Row)> refusals;
        long failedCount = 0;
        int failedTags = 0;
        IReadOnlyList<LogRecord> errors;
        IReadOnlyList<Activity> refusedSpans;
        await using (RelayHost host = await outbox.StartRelayAsync(clock))
        {
            refusals = await outbox.RefuseFiveTimesAsync(clock, refused.Id);
            // Two more idle loops: the dispute's later row stays held behind the Failed one.
            await clock.StepAsync(TimeSpan.FromSeconds(10));
            await clock.StepAsync(TimeSpan.FromSeconds(10));

            host.Services.GetRequiredService<MeterProvider>().ForceFlush();
            Metric failed = host.Metrics.Snapshot().Last(metric => metric.Name == "disputedesk.outbox.failed");
            foreach (ref readonly MetricPoint point in failed.GetMetricPoints())
            {
                failedCount += point.GetSumLong();
                failedTags += point.Tags.Count;
            }

            // The relay's own error lines; librdkafka's log lines keep their own level and are not the relay's.
            errors = [.. host.Logs.Snapshot().Where(record =>
                record.LogLevel >= LogLevel.Error && record.FormattedMessage?.StartsWith("Kafka client", StringComparison.Ordinal) != true)];
            refusedSpans = host.ProducerSpansFor(refused.Id);
        }

        // Refusals 1 to 4: 1, 2, 4 then 8 seconds after the batch's instant, each times 0.8 to 1.2; Postgres keeps
        // microseconds, so the stored value may be up to one microsecond below the computed one.
        for (int refusal = 0; refusal < 4; refusal++)
        {
            (DateTimeOffset refusedAt, OutboxMessage row) = refusals[refusal];
            var delay = TimeSpan.FromSeconds(Math.Pow(2, refusal));
            Assert.Equal((OutboxStatus.Pending, refusal + 1, "MsgSizeTooLarge"), (row.Status, row.Attempts, row.LastError));
            Assert.InRange(row.NextAttemptAtUtc, refusedAt + (delay * 0.8) - TimeSpan.FromMicroseconds(1), refusedAt + (delay * 1.2));
        }

        OutboxMessage fifth = refusals[4].Row;
        Assert.Equal((OutboxStatus.Failed, 5, "MsgSizeTooLarge"), (fifth.Status, fifth.Attempts, fifth.LastError));
        Assert.Equal(refusals[3].Row.NextAttemptAtUtc, fifth.NextAttemptAtUtc);
        Assert.Equal(OutboxStatus.Failed, (await outbox.RowAsync(refused.Id)).Status);

        Assert.Equal((1L, 0), (failedCount, failedTags));
        LogRecord error = Assert.Single(errors);
        Assert.Null(error.Exception);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["EventId"] = refused.Id.ToString(),
                ["DisputeId"] = raised.DisputeId.ToString(),
            },
            (error.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").ToDictionary(attribute => attribute.Key, attribute => attribute.Value?.ToString()));

        // The dispute's later row waits for an operator; only version 1 reached the topic.
        OutboxMessage held = await outbox.RowAsync(later.Id);
        Assert.Equal((OutboxStatus.Pending, 0), (held.Status, held.Attempts));
        ConsumeResult<string, string> record = Assert.Single(outbox.RecordsFor(raised.DisputeId));
        Assert.Equal(raised.Payload, record.Message.Value);

        // Each refused send's span adds only error.type, the Kafka error code, to story 4.1's attributes.
        Assert.Equal(5, refusedSpans.Count);
        Assert.All(refusedSpans, span =>
        {
            Assert.Equal(ActivityStatusCode.Error, span.Status);
            Assert.Equal("MsgSizeTooLarge", span.GetTagItem("error.type"));
            Assert.Equal(
                (string[])["error.type", "messaging.destination.name", "messaging.kafka.message.key", "messaging.message.id", "messaging.operation.name", "messaging.operation.type", "messaging.system"],
                (string[])[.. span.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal)]);
        });
    }
}
