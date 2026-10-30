using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Tests.Disputes;

/// <summary>
/// AD-18, FR-26: <see cref="Dispute.RecordSlaWarning"/> appends one <see cref="SlaWarningRaised"/> when the SLA is due 48
/// hours or less from now and not yet due, on a submitted or under-review dispute that has no warning yet; it refuses
/// every other case, so a dispute is warned at most once.
/// </summary>
public class DisputeSlaWarningTests
{
    private static readonly DateTimeOffset RaisedAt = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DueAt = RaisedAt + Dispute.Sla;
    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond);
    private static readonly Actor Sweep = new(ActorType.Automation, Actor.SlaSweepId);
    private static readonly Guid DisputeId = Guid.CreateVersion7(RaisedAt);

    private static readonly TransactionSnapshot Disputable = new(
        "ACC-A-1", "TXN-A-0029", "0011", RaisedAt.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", null,
        TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);

    [Fact]
    public void SlaWarningLeadTime_Is48Hours()
    {
        Assert.Equal(TimeSpan.FromHours(48), Dispute.SlaWarningLeadTime);
    }

    /// <summary>The instants either side of both edges of the window: 48 hours before the due time, and the due time.</summary>
    public static TheoryData<string, TimeSpan, bool> Instants => new()
    {
        { "48 hours and 1 µs before due", -(Dispute.SlaWarningLeadTime + Microsecond), false },
        { "exactly 48 hours before due", -Dispute.SlaWarningLeadTime, true },
        { "1 µs before due", -Microsecond, true },
        { "exactly due", TimeSpan.Zero, false },
        { "1 µs after due", Microsecond, false },
    };

    [Theory]
    [MemberData(nameof(Instants))]
    public void RecordSlaWarning_OnSubmittedAtEachEdge_SucceedsOnlyWithinTheLeadTimeBeforeDue(string why, TimeSpan fromDue, bool expected)
    {
        Dispute dispute = DisputeIn(DisputeStatus.Submitted);

        Result result = dispute.RecordSlaWarning(Sweep, DueAt + fromDue);

        Assert.True(expected == result.IsSuccess, why);
        Assert.Equal(expected ? 2 : 1, dispute.Version);
        Assert.Equal(expected ? 1 : 0, dispute.PendingChanges.Count);
    }

    [Theory]
    [InlineData(DisputeStatus.Submitted)]
    [InlineData(DisputeStatus.UnderReview)]
    public void RecordSlaWarning_OpenDisputeWithinTheLeadTime_AppendsOneWarningAndKeepsTheStatus(DisputeStatus status)
    {
        Dispute dispute = DisputeIn(status);
        int version = dispute.Version;
        DateTimeOffset now = DueAt - TimeSpan.FromHours(1);

        Result result = dispute.RecordSlaWarning(Sweep, now);

        Assert.True(result.IsSuccess);
        SlaWarningRaised warning = Assert.IsType<SlaWarningRaised>(Assert.Single(dispute.PendingChanges));
        Assert.Equal(DisputeId, warning.DisputeId);
        Assert.Equal(version + 1, warning.Version);
        Assert.Equal(now, warning.OccurredAtUtc);
        Assert.Equal(ActorType.Automation, warning.ActorType);
        Assert.Equal("worker:sla-sweep", warning.ActorId);
        Assert.Equal(status, dispute.Status);
        Assert.Equal(version + 1, dispute.Version);
        Assert.Equal(now, dispute.SlaWarningRaisedAtUtc);
    }

    [Theory]
    [InlineData(DisputeStatus.Escalated)]
    [InlineData(DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.Withdrawn)]
    public void RecordSlaWarning_ClosedOrEscalatedWithinTheLeadTime_IsRefused(DisputeStatus status)
    {
        Dispute dispute = DisputeIn(status);

        Result result = dispute.RecordSlaWarning(Sweep, DueAt - TimeSpan.FromHours(1));

        Assert.Equal(DisputeErrors.ActionNotAllowed, result.Error);
        Assert.Empty(dispute.PendingChanges);
        Assert.Null(dispute.SlaWarningRaisedAtUtc);
    }

    [Fact]
    public void RecordSlaWarning_Twice_RefusesTheSecond()
    {
        Dispute dispute = DisputeIn(DisputeStatus.Submitted);
        Assert.True(dispute.RecordSlaWarning(Sweep, DueAt - TimeSpan.FromHours(2)).IsSuccess);

        Result second = dispute.RecordSlaWarning(Sweep, DueAt - TimeSpan.FromHours(1));

        Assert.Equal(DisputeErrors.ActionNotAllowed, second.Error);
        Assert.Single(dispute.PendingChanges);
        Assert.Equal(DueAt - TimeSpan.FromHours(2), dispute.SlaWarningRaisedAtUtc);
    }

    [Fact]
    public void RecordSlaWarning_OnAReplayedStreamThatHoldsAWarning_RefusesASecond()
    {
        Dispute warned = DisputeIn(DisputeStatus.Submitted);
        Assert.True(warned.RecordSlaWarning(Sweep, DueAt - TimeSpan.FromHours(2)).IsSuccess);
        var replayed = Dispute.Replay([RaisedEvent(), .. warned.PendingChanges]);

        Result second = replayed.RecordSlaWarning(Sweep, DueAt - TimeSpan.FromHours(1));

        Assert.Equal(DisputeErrors.ActionNotAllowed, second.Error);
        Assert.Empty(replayed.PendingChanges);
    }

    [Fact]
    public void Escalate_AfterAWarning_StillEscalatesAndKeepsTheWarningInstant()
    {
        Dispute dispute = DisputeIn(DisputeStatus.UnderReview);
        DateTimeOffset warnedAt = DueAt - TimeSpan.FromHours(1);
        Assert.True(dispute.RecordSlaWarning(Sweep, warnedAt).IsSuccess);

        Assert.True(dispute.Escalate(Sweep, DueAt).IsSuccess);

        Assert.Equal(DisputeStatus.Escalated, dispute.Status);
        Assert.Equal(4, dispute.Version);
        Assert.Equal(warnedAt, dispute.SlaWarningRaisedAtUtc);
    }

    /// <summary>A dispute whose stream reaches <paramref name="status"/>, through the stored events that lead there.</summary>
    private static Dispute DisputeIn(DisputeStatus status) => Dispute.Replay(status switch
    {
        DisputeStatus.Submitted => [RaisedEvent()],
        DisputeStatus.UnderReview => [RaisedEvent(), Started()],
        DisputeStatus.Escalated => [RaisedEvent(), new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated) { DisputeId = DisputeId, Version = 2, OccurredAtUtc = RaisedAt }],
        DisputeStatus.Resolved => [RaisedEvent(), Started(), new DisputeResolved(DisputeStatus.UnderReview, DisputeStatus.Resolved, Outcome.Upheld, "Refunded") { DisputeId = DisputeId, Version = 3, OccurredAtUtc = RaisedAt }],
        DisputeStatus.Withdrawn => [RaisedEvent(), new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null) { DisputeId = DisputeId, Version = 2, OccurredAtUtc = RaisedAt }],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
    });

    private static DisputeRaised RaisedEvent() =>
        new("CUS-000001", Disputable, Reason.Unrecognised, "Not me", null, 87.23m, DisputeStatus.Submitted)
        {
            DisputeId = DisputeId,
            Version = 1,
            OccurredAtUtc = RaisedAt,
            ActorType = ActorType.Customer,
            ActorId = "login-a",
        };

    private static ReviewStarted Started() =>
        new(DisputeStatus.Submitted, DisputeStatus.UnderReview) { DisputeId = DisputeId, Version = 2, OccurredAtUtc = RaisedAt };
}
