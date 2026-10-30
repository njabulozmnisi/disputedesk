using System.Diagnostics;
using Confluent.Kafka;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 1 of story 4.2: a Kafka outage is transient. A failed send leaves its row Pending with no attempt counted,
/// the loop backs off (at most 10 seconds, <c>Backoff</c>'s theory in <see cref="OutboxRelayDependencyTests"/>) and keeps its
/// heartbeat fresh, and once the broker returns every waiting row is published in per-dispute order (AD-17, Failures; AD-25).
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayBrokerOutageTests(OutboxFixture outbox)
{
    private const string SendDeferred = "Outbox relay could not send an event";

    [Fact]
    public async Task Relay_BrokerPausedWhileDisputesAreRaised_KeepsRowsPendingWithNoAttemptThenPublishesThemInOrder()
    {
        // The test clock follows real time, because librdkafka times the paused sends out on its own, real, clock.
        RelayClock clock = OutboxFixture.NewClock();
        DateTime start = clock.GetUtcNow().UtcDateTime;
        using var stopTicking = new CancellationTokenSource();
        Task ticking = OutboxFixture.FollowRealTimeAsync(clock, stopTicking.Token);
        File.Delete(OutboxRelay.HeartbeatPath);
        OutboxMessage[] rows;
        List<string> failedSendCodes;
        TimeSpan oldestHeartbeat = TimeSpan.Zero;

        await using (RelayHost host = await outbox.StartRelayAsync(clock))
        {
            await OutboxFixture.WaitUntilAsync(() => host.LogLines.Any(line => line.StartsWith("Outbox relay holds the outbox lock", StringComparison.Ordinal)));
            await outbox.PauseBrokerAsync();
            try
            {
                DateTimeOffset at = clock.GetUtcNow();
                OutboxMessage first = await outbox.RaiseAsync(at);
                rows =
                [
                    first,
                    await outbox.AddVersionAsync(first.DisputeId, 2, at),
                    await outbox.AddVersionAsync(first.DisputeId, 3, at),
                    await outbox.RaiseAsync(at),
                ];

                // Two failed sends, the second after a backoff; each takes the 10-second delivery timeout.
                var waiting = Stopwatch.StartNew();
                while (SendDeferredLines(host).Count < 2)
                {
                    Assert.True(waiting.Elapsed < TimeSpan.FromSeconds(60), "The relay did not fail two sends.");
                    TimeSpan age = clock.GetUtcNow().UtcDateTime - (HeartbeatAt() ?? start);
                    oldestHeartbeat = age > oldestHeartbeat ? age : oldestHeartbeat;
                    await Task.Delay(50, TestContext.Current.CancellationToken);
                }

                foreach (OutboxMessage row in rows)
                {
                    OutboxMessage during = await outbox.RowAsync(row.Id);
                    Assert.Equal((OutboxStatus.Pending, 0, (string?)null), (during.Status, during.Attempts, during.LastError));
                }
            }
            finally
            {
                await outbox.UnpauseBrokerAsync();
            }

            await outbox.WaitUntilSentAsync(rows.Select(row => row.Id));

            // One warning per failed send, carrying the error code only.
            IReadOnlyList<LogRecord> deferred = SendDeferredLines(host);
            Assert.All(deferred, record =>
            {
                Assert.Equal(LogLevel.Warning, record.LogLevel);
                Assert.Equal(["ErrorCode", "{OriginalFormat}"], (string[])[.. (record.Attributes ?? []).Select(attribute => attribute.Key).Order(StringComparer.Ordinal)]);
            });
            failedSendCodes = [.. host.ProducerSpans.Where(span => span.Status == ActivityStatusCode.Error).Select(span => span.GetTagItem("error.type") as string ?? "")];
            Assert.Equal(deferred.Count, failedSendCodes.Count);
        }

        await stopTicking.CancelAsync();
        await ticking;

        // Recorded for the report: the codes the paused broker produced against the pinned Redpanda image.
        TestContext.Current.TestOutputHelper?.WriteLine($"Failed send codes: {string.Join(", ", failedSendCodes.Distinct())}");
        Assert.InRange(oldestHeartbeat, TimeSpan.Zero, TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
        foreach (OutboxMessage row in rows)
        {
            OutboxMessage after = await outbox.RowAsync(row.Id);
            Assert.Equal((OutboxStatus.Sent, 0, (string?)null), (after.Status, after.Attempts, after.LastError));
        }

        // A send that timed out while paused may still have reached the broker, so a version can appear twice, with
        // its one CloudEvent id; the first copy of each version is in order.
        List<ConsumeResult<string, string>> records = [.. outbox.RecordsFor(rows[0].DisputeId).OrderBy(record => record.Offset.Value)];
        Assert.Equal([1, 2, 3], (int[])[.. records.Select(OutboxFixture.DisputeVersion).Distinct()]);
        Assert.All(records.GroupBy(OutboxFixture.DisputeVersion), copies => Assert.Single(copies.Select(OutboxFixture.CloudEventId).Distinct()));
        Assert.NotEmpty(outbox.RecordsFor(rows[3].DisputeId));
    }

    private static IReadOnlyList<LogRecord> SendDeferredLines(RelayHost host) =>
        [.. host.Logs.Snapshot().Where(record => record.FormattedMessage?.StartsWith(SendDeferred, StringComparison.Ordinal) == true)];

    private static DateTime? HeartbeatAt() =>
        File.Exists(OutboxRelay.HeartbeatPath) ? File.GetLastWriteTimeUtc(OutboxRelay.HeartbeatPath) : null;
}
