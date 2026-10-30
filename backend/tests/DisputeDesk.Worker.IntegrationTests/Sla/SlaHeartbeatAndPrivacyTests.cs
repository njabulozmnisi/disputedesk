using DisputeDesk.Domain.Disputes;
using DisputeDesk.Worker.IntegrationTests.Outbox;
using DisputeDesk.Worker.Sla;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// AC7 (R27; NFR-RES-1, AD-25): the loop runs every 30 seconds on a <c>PeriodicTimer</c> built with the injected clock and
/// touches its heartbeat each loop. AC8 (R29, R36; NFR-SEC-5, AD-20): the sweep's logs, spans and metrics carry counts and
/// at most dispute IDs, never the customer's data.
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaHeartbeatAndPrivacyTests(SlaFixture fixture)
{
    [Fact]
    public async Task Loop_OnTheInjectedClock_RunsEvery30SecondsAndTouchesTheHeartbeatEachLoop()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        DateTimeOffset start = rig.Clock.GetUtcNow();
        SlaSweep sweep = rig.Sweep;

        await sweep.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            // The first loop runs at once: an escalation batch and a warning batch, then the heartbeat at the clock's time.
            await OutboxFixture.WaitUntilAsync(() => Loops(rig) == 1 && HeartbeatAt() == start.UtcDateTime);

            rig.Clock.Advance(TimeSpan.FromSeconds(30) - SweepRig.Microsecond);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.Equal(1, Loops(rig));
            Assert.Equal(start.UtcDateTime, HeartbeatAt());

            rig.Clock.Advance(SweepRig.Microsecond);
            await OutboxFixture.WaitUntilAsync(() => Loops(rig) == 2 && HeartbeatAt() == start.AddSeconds(30).UtcDateTime);

            rig.Clock.Advance(TimeSpan.FromSeconds(30));
            await OutboxFixture.WaitUntilAsync(() => Loops(rig) == 3 && HeartbeatAt() == start.AddSeconds(60).UtcDateTime);
        }
        finally
        {
            await sweep.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Sweep_OverASeededCustomersDisputes_LogsSpansAndMetricsCarryNoCustomerData()
    {
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        DateTimeOffset now = SweepRig.RaisedAt + Dispute.Sla;
        Guid escalated = await rig.RaiseAsync(SweepRig.RaisedAt);
        Guid warned = await rig.RaiseAsync(now + TimeSpan.FromHours(1) - Dispute.Sla, DisputeStatus.UnderReview);
        string transactionId = (await rig.DisputeRowAsync(escalated)).TransactionId;
        rig.Clock.SetUtcNow(now);

        await rig.SweepAsync();

        Assert.Single(await rig.StreamAsync(escalated), row => row.EventType == nameof(DisputeEscalated));
        Assert.Single(await rig.StreamAsync(warned), row => row.EventType == nameof(SlaWarningRaised));
        string[] forbidden = [SweepRig.CustomerId, SweepRig.AccountRef, transactionId, "TXN-SLA-", "Shop SLA description", "login-sla"];
        IEnumerable<string> spanText = rig.Spans.Snapshot()
            .Where(span => span.OperationName == SlaSweep.BatchSpanName)
            .SelectMany(span => span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}").Append(span.DisplayName).Append(span.StatusDescription ?? ""));
        IEnumerable<string> metricText = rig.Measurements.SelectMany(measurement => measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
        IEnumerable<string> logText = rig.LogLines.Select(line => line.Line);
        foreach (string text in spanText.Concat(metricText).Concat(logText))
        {
            Assert.DoesNotContain(forbidden, part => text.Contains(part, StringComparison.Ordinal));
        }

        // The batch spans carry only the claim and two counts.
        Assert.All(
            rig.Spans.Snapshot().Where(span => span.OperationName == SlaSweep.BatchSpanName),
            span => Assert.Equal(["disputedesk.sla_sweep.changed", "disputedesk.sla_sweep.claim", "disputedesk.sla_sweep.claimed"], span.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal)));
        Assert.Contains(rig.SweepLogLines, line => line.Contains("changed 1 of 1 claimed", StringComparison.Ordinal));
    }

    /// <summary>Complete loops so far: each ends with its warning batch.</summary>
    private static int Loops(SweepRig rig) => rig.BatchSpans("warning").Count;

    private static DateTime HeartbeatAt() => File.GetLastWriteTimeUtc(SlaSweep.HeartbeatPath);
}
