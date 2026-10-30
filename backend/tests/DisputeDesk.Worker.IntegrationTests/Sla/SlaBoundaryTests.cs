using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Worker.Sla;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// AC1 (R22; FR-26, NFR-TEST-2): the sweep escalates a submitted or under-review dispute at exactly 240 hours after it
/// was raised and not a microsecond before, through <c>Dispute.Escalate</c> on the replayed dispute, as the automation
/// actor <c>worker:sla-sweep</c>; escalated, resolved and withdrawn disputes past the SLA are left as they are.
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaBoundaryTests(SlaFixture fixture)
{
    [Fact]
    public async Task Sweep_OneMicrosecondBeforeThenAtTheSla_EscalatesSubmittedAndUnderReviewOnlyAtTheSla()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid submitted = await rig.RaiseAsync(SweepRig.RaisedAt);
        Guid underReview = await rig.RaiseAsync(SweepRig.RaisedAt, DisputeStatus.UnderReview);
        DateTimeOffset dueAt = SweepRig.RaisedAt + Dispute.Sla;

        rig.Clock.SetUtcNow(dueAt - SweepRig.Microsecond);
        await rig.SweepAsync();

        Assert.Equal(DisputeStatus.Submitted, (await rig.DisputeRowAsync(submitted)).Status);
        Assert.Equal(DisputeStatus.UnderReview, (await rig.DisputeRowAsync(underReview)).Status);
        Assert.DoesNotContain(await rig.StreamAsync(submitted), row => row.EventType == nameof(DisputeEscalated));
        Assert.DoesNotContain(await rig.StreamAsync(underReview), row => row.EventType == nameof(DisputeEscalated));

        rig.Clock.SetUtcNow(dueAt);
        await rig.SweepAsync();

        foreach ((Guid disputeId, DisputeStatus from) in new[] { (submitted, DisputeStatus.Submitted), (underReview, DisputeStatus.UnderReview) })
        {
            List<StoredEventRow> stream = await rig.StreamAsync(disputeId);
            StoredEventRow row = Assert.Single(stream, row => row.EventType == nameof(DisputeEscalated));
            // The next version after the warning the first run recorded: the escalation was decided on the replayed stream.
            Assert.Equal(stream.Count, row.Version);
            Assert.Equal(ActorType.Automation, row.ActorType);
            Assert.Equal("worker:sla-sweep", row.ActorId);
            Assert.Equal(dueAt, row.OccurredAtUtc);
            DisputeEscalated escalated = Assert.IsType<DisputeEscalated>(row.ToStoredEvent());
            Assert.Equal(from, escalated.FromStatus);
            Dispute dispute = await rig.DisputeRowAsync(disputeId);
            Assert.Equal(DisputeStatus.Escalated, dispute.Status);
            Assert.Equal(row.Version, dispute.Version);
        }
    }

    [Theory]
    [InlineData(DisputeStatus.Escalated)]
    [InlineData(DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.Withdrawn)]
    public async Task Sweep_PastTheSla_LeavesEscalatedResolvedAndWithdrawnUnchanged(DisputeStatus status)
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt, status);
        List<StoredEventRow> before = await rig.StreamAsync(disputeId);
        rig.Clock.SetUtcNow(SweepRig.RaisedAt + Dispute.Sla + TimeSpan.FromDays(3));

        await rig.SweepAsync();

        Assert.Equal(before.Count, (await rig.StreamAsync(disputeId)).Count);
        Dispute row = await rig.DisputeRowAsync(disputeId);
        Assert.Equal(status, row.Status);
        Assert.Equal(before.Count, row.Version);
        Assert.Null(row.SlaWarningRaisedAtUtc);
    }

    [Fact]
    public void SweepActor_IsTheAutomationActorWorkerSlaSweep()
    {
        Assert.Equal(new Actor(ActorType.Automation, "worker:sla-sweep"), SlaSweep.SweepActor);
    }
}
