using System.Diagnostics;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// AC6 (R22, R18, R29; FR-26, FR-17, FR-30, NFR-API-5): one <c>SlaWarningRaised</c> per open dispute whose SLA is due 48
/// hours or less from now and not yet due, recorded after the escalation claims, never published, and replayed into
/// the <c>Disputes</c> row's <c>SlaWarningRaisedAtUtc</c>.
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaWarningTests(SlaFixture fixture)
{
    private static readonly DateTimeOffset Now = SweepRig.RaisedAt + Dispute.Sla;

    [Fact]
    public async Task SweepTwice_AtEachEdgeOfTheLeadTime_WarnsOnlyWithin48HoursAndBeforeDueAndOnlyOnce()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        // For each due time, a submitted and an under-review dispute raised 240 hours before it.
        var dueIn = new Dictionary<string, TimeSpan>
        {
            ["48 hours"] = Dispute.SlaWarningLeadTime,
            ["48 hours and 1 µs"] = Dispute.SlaWarningLeadTime + SweepRig.Microsecond,
            ["1 µs"] = SweepRig.Microsecond,
            ["now"] = TimeSpan.Zero,
        };
        var disputes = new Dictionary<string, Guid[]>();
        foreach ((string name, TimeSpan due) in dueIn)
        {
            DateTimeOffset raisedAt = Now + due - Dispute.Sla;
            disputes[name] = [await rig.RaiseAsync(raisedAt), await rig.RaiseAsync(raisedAt, DisputeStatus.UnderReview)];
        }

        rig.Clock.SetUtcNow(Now);
        await rig.SweepAsync();
        await rig.SweepAsync();

        foreach (Guid disputeId in disputes["48 hours"].Concat(disputes["1 µs"]))
        {
            List<StoredEventRow> stream = await rig.StreamAsync(disputeId);
            StoredEventRow warning = Assert.Single(stream, row => row.EventType == nameof(SlaWarningRaised));
            Assert.Equal(stream.Count, warning.Version);
            Assert.Equal(Now, warning.OccurredAtUtc);
            Assert.Equal(ActorType.Automation, warning.ActorType);
            Assert.Equal("worker:sla-sweep", warning.ActorId);
            Dispute row = await rig.DisputeRowAsync(disputeId);
            Assert.Equal(Now, row.SlaWarningRaisedAtUtc);
            Assert.Equal(warning.Version, row.Version);
            Assert.NotEqual(DisputeStatus.Escalated, row.Status);
            // Never published: the dispute's outbox rows are its earlier events' only.
            Assert.DoesNotContain(await rig.OutboxAsync(disputeId), message => message.Id == warning.Id);
            Assert.Equal(stream.Count - 1, (await rig.OutboxAsync(disputeId)).Count);
        }

        foreach (Guid disputeId in disputes["48 hours and 1 µs"])
        {
            Assert.DoesNotContain(await rig.StreamAsync(disputeId), row => row.EventType == nameof(SlaWarningRaised));
            Assert.Null((await rig.DisputeRowAsync(disputeId)).SlaWarningRaisedAtUtc);
        }

        foreach (Guid disputeId in disputes["now"])
        {
            List<StoredEventRow> stream = await rig.StreamAsync(disputeId);
            Assert.DoesNotContain(stream, row => row.EventType == nameof(SlaWarningRaised));
            Assert.Single(stream, row => row.EventType == nameof(DisputeEscalated));
        }

        // The escalation claims run first, then the warning claim, in each loop.
        IReadOnlyList<Activity> batches = [.. rig.Spans.Snapshot().Where(span => span.OperationName == "disputedesk.sla_sweep.batch")];
        Assert.Equal(["overdue", "warning", "overdue", "warning"], batches.Select(span => span.GetTagItem("disputedesk.sla_sweep.claim") as string));
        Assert.Equal(4, (int?)rig.BatchSpans("warning")[0].GetTagItem("disputedesk.sla_sweep.changed"));
    }

    [Fact]
    public async Task ClaimWarnings_AfterTheWarningIsRecorded_SkipsTheWarnedDispute()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid warned = await rig.RaiseAsync(Now + TimeSpan.FromHours(1) - Dispute.Sla);
        rig.Clock.SetUtcNow(Now);
        await using (AppDbContext db = rig.CreateDbContext())
        {
            await using IDbContextTransaction before = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Assert.Equal([warned], await db.ClaimWarningsAsync(Now, Dispute.SlaWarningLeadTime, TestContext.Current.CancellationToken));
        }

        await rig.SweepAsync();

        await using AppDbContext after = rig.CreateDbContext();
        await using IDbContextTransaction transaction = await after.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await after.ClaimWarningsAsync(Now, Dispute.SlaWarningLeadTime, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Replay_StreamsWithAWarningCommittedByTheSweep_ReproduceTheirDisputesRowsWithTheWarningInstant()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid warnedOnly = await rig.RaiseAsync(Now + TimeSpan.FromHours(1) - Dispute.Sla, DisputeStatus.UnderReview);
        Guid warnedThenEscalated = await rig.RaiseAsync(Now + TimeSpan.FromHours(2) - Dispute.Sla);
        rig.Clock.SetUtcNow(Now);
        await rig.SweepAsync();
        rig.Clock.SetUtcNow(Now + TimeSpan.FromHours(2));
        await rig.SweepAsync();
        await using AppDbContext db = rig.CreateDbContext();
        IEntityType disputeType = db.Model.FindEntityType(typeof(Dispute)) ?? throw new InvalidOperationException("Dispute is not mapped.");

        foreach (Guid disputeId in new[] { warnedOnly, warnedThenEscalated })
        {
            List<StoredEventRow> rows = await rig.StreamAsync(disputeId);
            Assert.Contains(rows, row => row.EventType == nameof(SlaWarningRaised));
            Dispute row = await rig.DisputeRowAsync(disputeId);

            var replayed = Dispute.Replay([.. rows.Select(stored => stored.ToStoredEvent())]);

            Assert.NotNull(row.SlaWarningRaisedAtUtc);
            Assert.All(disputeType.GetProperties(), column =>
                Assert.Equal(column.PropertyInfo?.GetValue(row), column.PropertyInfo?.GetValue(replayed)));
        }

        Assert.Equal(DisputeStatus.Escalated, (await rig.DisputeRowAsync(warnedThenEscalated)).Status);
    }
}
