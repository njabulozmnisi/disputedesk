using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>Criterion 2: per dispute, only the head row is produced, and the next version only after it is Sent (AD-17, Order).</summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayOrderTests(OutboxFixture outbox)
{
    [Fact]
    public async Task Relay_ThreeVersionsOfOneDispute_ReachTheTopicInVersionOrder()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        DateTimeOffset at = clock.GetUtcNow().AddMinutes(-1);
        OutboxMessage first = await outbox.RaiseAsync(at);
        OutboxMessage second = await outbox.AddVersionAsync(first.DisputeId, 2, at);
        OutboxMessage third = await outbox.AddVersionAsync(first.DisputeId, 3, at);
        OutboxMessage other = await outbox.RaiseAsync(at);

        await using (await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([first.Id, second.Id, third.Id, other.Id]);
        }

        // One key, one partition: the offsets are the order the broker appended them.
        int[] versions = [.. outbox.RecordsFor(first.DisputeId).OrderBy(record => record.Offset.Value).Select(OutboxFixture.DisputeVersion)];
        Assert.Equal([1, 2, 3], versions);
        Assert.Single(outbox.RecordsFor(other.DisputeId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_HeadRowFailedOrNotYetDue_HoldsBackThatDisputeOnly(bool failed)
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        DateTimeOffset at = clock.GetUtcNow().AddMinutes(-1);
        OutboxMessage head = await outbox.RaiseAsync(at);
        OutboxMessage held = await outbox.AddVersionAsync(head.DisputeId, 2, at);
        OutboxMessage other = await outbox.RaiseAsync(at);
        await outbox.UpdateRowAsync(head.Id, row =>
        {
            if (failed)
            {
                row.Status = OutboxStatus.Failed;
            }
            else
            {
                row.NextAttemptAtUtc = clock.GetUtcNow().AddHours(1);
            }
        });

        await using (await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([other.Id]);
        }

        Assert.Equal(OutboxStatus.Pending, (await outbox.RowAsync(held.Id)).Status);
        Assert.Empty(outbox.RecordsFor(head.DisputeId));
        Assert.Single(outbox.RecordsFor(other.DisputeId));
    }
}
