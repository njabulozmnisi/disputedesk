using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>Criterion 1: a due row is published byte for byte and marked Sent only after its delivery report (AD-17).</summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayPublishTests(OutboxFixture outbox)
{
    [Fact]
    public async Task Relay_RowDueExactlyNow_PublishesPayloadByteForByteKeyedByDisputeWithExactHeaders()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        // 1.7 microseconds past the second: PostgreSQL keeps microseconds, and Npgsql drops the 7 ticks below them.
        clock.Advance(TimeSpan.FromTicks(17));
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset stored = now.AddTicks(-(now.Ticks % 10));
        // Due at exactly the batch's instant: "NextAttemptAtUtc" <= @now includes the boundary.
        OutboxMessage before = await outbox.RaiseAsync(now);

        await using (await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([before.Id]);
        }

        ConsumeResult<string, string> record = Assert.Single(outbox.RecordsFor(before.DisputeId));
        Assert.Equal(before.DisputeId.ToString(), record.Message.Key);
        Assert.Equal(before.Payload, record.Message.Value);
        Assert.Equal(["content-type", "traceparent"], record.Message.Headers.Select(header => header.Key).Order());
        Assert.Equal("application/cloudevents+json", Encoding.UTF8.GetString(record.Message.Headers.GetLastBytes("content-type")));
        using var cloudEvent = JsonDocument.Parse(record.Message.Value);
        Assert.Equal(before.Id.ToString(), cloudEvent.RootElement.GetProperty("id").GetString());

        OutboxMessage after = await outbox.RowAsync(before.Id);
        Assert.Equal(OutboxStatus.Sent, after.Status);
        Assert.Equal(stored, after.SentAtUtc);
        Assert.Equal(
            (before.Id, before.EventType, before.DisputeId, before.DisputeVersion, before.Payload, before.TraceParent, before.TraceState),
            (after.Id, after.EventType, after.DisputeId, after.DisputeVersion, after.Payload, after.TraceParent, after.TraceState));
        Assert.Equal((0, before.NextAttemptAtUtc, (string?)null, before.CreatedAtUtc), (after.Attempts, after.NextAttemptAtUtc, after.LastError, after.CreatedAtUtc));
    }

    [Fact]
    public async Task Relay_RowDueOneSecondLater_IsNotPublished()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        OutboxMessage later = await outbox.RaiseAsync(clock.GetUtcNow().AddSeconds(1));
        OutboxMessage due = await outbox.RaiseAsync(clock.GetUtcNow());

        await using (await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([due.Id]);
        }

        Assert.Equal(OutboxStatus.Pending, (await outbox.RowAsync(later.Id)).Status);
        Assert.Empty(outbox.RecordsFor(later.DisputeId));
    }

    [Fact]
    public void CreateProducerConfig_FromOptions_IsIdempotentWithAcksAllAndATenSecondDeliveryTimeout()
    {
        ProducerConfig config = OutboxRelay.CreateProducerConfig(new KafkaOptions { BootstrapServers = "broker:9092" });

        Assert.Equal("broker:9092", config.BootstrapServers);
        Assert.True(config.EnableIdempotence);
        Assert.Equal(Acks.All, config.Acks);
        Assert.Equal(10_000, config.MessageTimeoutMs);
    }
}
