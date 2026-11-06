using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.Redpanda;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// The relay creates its topic, 3 partitions, before its first send, on a broker that has never had it; a later start
/// finds the topic there and publishes as before (AD-17). A broker that created the topic on the first send would give
/// it its own default of one partition.
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayTopicTests(OutboxFixture outbox)
{
    [Fact]
    public async Task Relay_BrokerWithoutTheTopic_CreatesItWithThreePartitionsBeforeTheFirstSend()
    {
        await using RedpandaContainer broker = new RedpandaBuilder(OutboxFixture.RedpandaImage).Build();
        await broker.StartAsync(TestContext.Current.CancellationToken);
        string bootstrapServers = new Uri(broker.GetBootstrapAddress()).Authority;
        FakeTimeProvider clock = OutboxFixture.NewClock();
        OutboxMessage first = await outbox.RaiseAsync(clock.GetUtcNow());

        await using (await RelayHost.StartAsync(clock, outbox.ConnectionString, bootstrapServers))
        {
            await outbox.WaitUntilSentAsync([first.Id], clock);
        }

        Assert.Equal(OutboxRelay.TopicPartitions, await PartitionCountAsync(bootstrapServers));
    }

    [Fact]
    public async Task Relay_TopicAlreadyThere_StartsAndPublishes()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        OutboxMessage row = await outbox.RaiseAsync(clock.GetUtcNow());

        // The fixture's broker has the topic from the start.
        await using (await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([row.Id], clock);
        }

        Assert.Single(outbox.RecordsFor(row.DisputeId));
        Assert.Equal(OutboxRelay.TopicPartitions, await PartitionCountAsync(outbox.BootstrapServers));
    }

    /// <summary>The topic's partitions, read through the admin API, which never creates a topic it is asked about.</summary>
    private static async Task<int> PartitionCountAsync(string bootstrapServers)
    {
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        DescribeTopicsResult described = await admin.DescribeTopicsAsync(TopicCollection.OfTopicNames([OutboxRelay.Topic]));
        return Assert.Single(described.TopicDescriptions).Partitions.Count;
    }
}
