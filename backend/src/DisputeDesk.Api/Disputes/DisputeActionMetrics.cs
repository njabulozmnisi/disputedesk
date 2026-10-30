using System.Diagnostics.Metrics;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Infrastructure.Observability;

namespace DisputeDesk.Api.Disputes;

/// <summary>
/// The dispute action metrics of AD-20 and NFR-OBS-3, created through <see cref="IMeterFactory"/> (Microsoft Learn:
/// "Creating metrics"). Labels carry only the action and the actor type, never personal data.
/// </summary>
public sealed class DisputeActionMetrics
{
    /// <summary>
    /// Advised to the exporter (Microsoft Learn, "Creating metrics": histogram advice), from 1 minute to 30 days, so the
    /// share withdrawn within 1 hour (SM-C3) is a bucket boundary.
    /// </summary>
    public static readonly double[] WithdrawAgeBuckets = [60, 300, 900, 1800, 3600, 7200, 21600, 86400, 604800, 2592000];

    private readonly Counter<long> _actions;
    private readonly Histogram<double> _withdrawAge;
    private readonly Counter<long> _resolved;

    public DisputeActionMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(Telemetry.MeterName);
        _actions = meter.CreateCounter<long>(
            "disputedesk.dispute_actions", description: "Dispute actions that succeeded, by action and actor type.");
        _withdrawAge = meter.CreateHistogram(
            "disputedesk.disputes.withdraw_age",
            unit: "s",
            description: "Time from raise to withdraw.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = WithdrawAgeBuckets });
        _resolved = meter.CreateCounter<long>(
            "disputedesk.disputes.resolved", description: "Disputes resolved, by outcome and whether the SLA sweep had escalated them.");
    }

    /// <summary>Counts an operations action that committed (AD-20): <paramref name="action"/> is <c>startReview</c>, <c>escalate</c> or <c>resolve</c>.</summary>
    public void RecordOperationsAction(string action) =>
        _actions.Add(1, new KeyValuePair<string, object?>("action", action), new KeyValuePair<string, object?>("actor_type", "operations"));

    /// <summary>Counts a committed resolve (SM-4): <paramref name="slaEscalated"/> is whether the SLA sweep had escalated the dispute.</summary>
    public void RecordResolved(Outcome outcome, bool slaEscalated) =>
        _resolved.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome == Outcome.Upheld ? "upheld" : "rejected"),
            new KeyValuePair<string, object?>("sla_escalated", slaEscalated ? "true" : "false"));

    /// <summary>Counts the customer's withdraw of <paramref name="dispute"/> and records its age at the withdraw.</summary>
    public void RecordWithdrawn(DisputeResponse dispute)
    {
        _actions.Add(1, new KeyValuePair<string, object?>("action", "withdraw"), new KeyValuePair<string, object?>("actor_type", "customer"));
        // A dispute is withdrawn at most once (FR-19), so this is the withdraw just committed.
        DateTimeOffset withdrawnAt = dispute.Timeline.Last(entry => entry.Action == DisputeAction.Withdraw).OccurredAt;
        _withdrawAge.Record((withdrawnAt - dispute.CreatedAt).TotalSeconds);
    }
}
