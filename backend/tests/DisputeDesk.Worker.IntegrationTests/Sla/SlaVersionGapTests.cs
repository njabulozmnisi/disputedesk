using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// Story 8.14, AC4 (D3; R23, R28, AD-19): the sweep only escalates, so every stored event of a dispute is published and
/// the versions written to the stored stream and the outbox have no gap. Delivery stays at least once (AD-17).
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaVersionGapTests(SlaFixture fixture)
{
    [Fact]
    public async Task SweepInsideTheOldLeadWindowThenAfterDue_WritesVersions1To3WithAnOutboxRowEachAndNoGap()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt, DisputeStatus.UnderReview);
        DateTimeOffset dueAt = SweepRig.RaisedAt + Dispute.Sla;

        // 24 hours before due: inside the 48-hour lead window the warning used; nothing is written.
        rig.Clock.SetUtcNow(dueAt - TimeSpan.FromHours(24));
        await rig.SweepAsync();
        Assert.Equal([1, 2], (await rig.StreamAsync(disputeId)).Select(row => row.Version));

        rig.Clock.SetUtcNow(dueAt);
        await rig.SweepAsync();

        List<StoredEventRow> stream = await rig.StreamAsync(disputeId);
        List<OutboxMessage> outbox = await rig.OutboxAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeEscalated)], stream.Select(row => row.EventType));
        Assert.Equal([1, 2, 3], stream.Select(row => row.Version));
        Assert.Equal(stream.Select(row => (row.Id, row.Version)), outbox.Select(message => (message.Id, message.DisputeVersion)));
    }
}
