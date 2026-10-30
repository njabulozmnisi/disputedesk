using System.Diagnostics;
using System.Text.Json;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Events;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// AC5 (R18, R23, R36; FR-17, FR-28, NFR-OBS-3): an SLA escalation as the customer sees it (prioritised, by the bank), as
/// it is published (one <c>status-changed.v1</c> by <c>automation</c>, traced to the batch span), and as it is counted
/// after the commit.
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaEscalationViewTests(SlaFixture fixture)
{
    [Fact]
    public async Task Escalation_TheCustomerSeesPrioritisedByTheBankAndOneStatusChangedByAutomationIsWrittenInTheBatchTrace()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt, DisputeStatus.UnderReview);
        rig.Clock.SetUtcNow(SweepRig.RaisedAt + Dispute.Sla);

        await rig.SweepAsync();

        List<StoredEventRow> rows = await rig.StreamAsync(disputeId);
        List<StoredEvent> stream = [.. rows.Select(row => row.ToStoredEvent())];
        Contracts.Disputes.DisputeResponse customer = CustomerView.ToResponse(await rig.DisputeRowAsync(disputeId), stream);
        Assert.Equal(Contracts.Disputes.CustomerStatus.UnderReviewPrioritised, customer.CustomerStatus);
        Contracts.Disputes.TimelineEntryResponse last = customer.Timeline[^1];
        Assert.Equal(Contracts.Disputes.DisputeAction.Escalate, last.Action);
        Assert.Equal(Contracts.Disputes.TimelineActor.TheBank, last.Actor);

        OutboxMessage published = Assert.Single(await rig.OutboxAsync(disputeId), message => message.Id == rows[^1].Id);
        Assert.Equal(DisputeEvents.StatusChangedV1, published.EventType);
        using var payload = JsonDocument.Parse(published.Payload);
        JsonElement data = payload.RootElement.GetProperty("data");
        Assert.Equal("escalate", data.GetProperty("action").GetString());
        Assert.Equal("automation", data.GetProperty("actorType").GetString());
        Assert.Equal("escalated", data.GetProperty("toStatus").GetString());
        Activity batch = Assert.Single(rig.BatchSpans("overdue"));
        Assert.Equal(batch.Id, published.TraceParent);
        Assert.Equal(batch.TraceId.ToHexString(), rows[^1].TraceId);

        Assert.Equal(1, rig.Total("disputedesk.sla.escalations"));
        Assert.Equal(1, rig.Total("disputedesk.dispute_actions", ("action", "escalate"), ("actor_type", "automation")));
    }

    [Fact]
    public async Task Escalation_OnAClockBetweenMicroseconds_StoresAndPublishesTheSameWholeMicrosecondInstant()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        Guid disputeId = await rig.RaiseAsync(SweepRig.RaisedAt);
        DateTimeOffset dueAt = SweepRig.RaisedAt + Dispute.Sla;
        // 1.5 µs past due: Postgres keeps whole microseconds, a DateTimeOffset 100 ns ticks (AD-4).
        rig.Clock.SetUtcNow(dueAt + TimeSpan.FromTicks(15));

        await rig.SweepAsync();

        StoredEventRow escalated = (await rig.StreamAsync(disputeId))[^1];
        Assert.Equal(dueAt + SweepRig.Microsecond, escalated.OccurredAtUtc);
        OutboxMessage published = Assert.Single(await rig.OutboxAsync(disputeId), message => message.Id == escalated.Id);
        using var payload = JsonDocument.Parse(published.Payload);
        Assert.Equal(escalated.OccurredAtUtc, payload.RootElement.GetProperty("data").GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(escalated.OccurredAtUtc, published.CreatedAtUtc);
    }
}
