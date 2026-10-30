using System.Reflection;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Tests.Disputes;

/// <summary>
/// AD-13: <see cref="Dispute.Replay"/> rebuilds a dispute from its stream through the same <c>Apply</c> cases its methods
/// use, and refuses a malformed stream rather than return a partial dispute.
/// </summary>
public class DisputeReplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor CustomerA = new(ActorType.Customer, "login-a");
    private static readonly Guid DisputeId = Guid.CreateVersion7(Now);

    private static readonly TransactionSnapshot Disputable = new(
        "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", "492181******4417",
        TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);

    [Fact]
    public void Replay_TheRaiseStream_GivesTheSameStateAsTheRaiseThatProducedIt()
    {
        Dispute raised = Dispute.Raise(
            "CUS-000001", Disputable, everDisputed: false, Reason.WrongAmount, "Charged twice", 80m, CustomerA, Now).Value;

        var replayed = Dispute.Replay(raised.PendingChanges);

        Assert.Equal(StateOf(raised), StateOf(replayed));
    }

    [Fact]
    public void Replay_AnyStream_AppendsNoPendingChange()
    {
        var replayed = Dispute.Replay([RaisedEvent()]);

        Assert.Empty(replayed.PendingChanges);
    }

    [Theory]
    [MemberData(nameof(LifecycleStreams))]
    public void Replay_LifecycleStream_ReachesTheStatusAndVersionOfItsLastStoredEvent(StoredEvent[] stream, DisputeStatus expected)
    {
        var dispute = Dispute.Replay(stream);

        Assert.Equal(expected, dispute.Status);
        Assert.Equal(stream[^1].Version, dispute.Version);
        Assert.Equal(RaisedEvent().DisputeId, dispute.Id);
    }

    public static TheoryData<StoredEvent[], DisputeStatus> LifecycleStreams => new()
    {
        { [RaisedEvent()], DisputeStatus.Submitted },
        { [RaisedEvent(), Started(2)], DisputeStatus.UnderReview },
        { [RaisedEvent(), Started(2), Escalated(3, DisputeStatus.UnderReview)], DisputeStatus.Escalated },
        { [RaisedEvent(), Escalated(2, DisputeStatus.Submitted)], DisputeStatus.Escalated },
        { [RaisedEvent(), Started(2), Resolved(3)], DisputeStatus.Resolved },
        { [RaisedEvent(), Withdrawn(2)], DisputeStatus.Withdrawn },
    };

    [Fact]
    public void Replay_StreamWithAnSlaWarning_SetsTheWarningInstantAndKeepsTheStatus()
    {
        Assert.Null(Dispute.Replay([RaisedEvent(), Started(2)]).SlaWarningRaisedAtUtc);
        var warning = new SlaWarningRaised { DisputeId = DisputeId, Version = 3, OccurredAtUtc = Now.AddDays(9) };

        var warned = Dispute.Replay([RaisedEvent(), Started(2), warning]);
        var escalated = Dispute.Replay([RaisedEvent(), Started(2), warning, Escalated(4, DisputeStatus.UnderReview)]);

        Assert.Equal(DisputeStatus.UnderReview, warned.Status);
        Assert.Equal(3, warned.Version);
        Assert.Equal(Now.AddDays(9), warned.SlaWarningRaisedAtUtc);
        Assert.Equal(DisputeStatus.Escalated, escalated.Status);
        Assert.Equal(Now.AddDays(9), escalated.SlaWarningRaisedAtUtc);
    }

    [Fact]
    public void Replay_ResolveStream_TakesTheOutcomeOfDisputeResolved()
    {
        Assert.Null(Dispute.Replay([RaisedEvent(), Started(2)]).Outcome);

        var resolved = Dispute.Replay([RaisedEvent(), Started(2), Resolved(3)]);

        Assert.Equal(Outcome.Upheld, resolved.Outcome);
    }

    [Theory]
    [MemberData(nameof(MalformedStreams))]
    public void Replay_MalformedStream_Throws(string why, StoredEvent[] stream)
    {
        Assert.Throws<InvalidOperationException>(() => Dispute.Replay(stream));
        Assert.NotEmpty(why);
    }

    public static TheoryData<string, StoredEvent[]> MalformedStreams => new()
    {
        { "empty", [] },
        { "starts with another type", [Started(1)] },
        { "skips a version", [RaisedEvent(), Started(3)] },
        { "repeats a version", [RaisedEvent(), Started(1)] },
        { "out of order", [Started(2), RaisedEvent()] },
        { "raised twice", [RaisedEvent(), RaisedEvent() with { Version = 2 }] },
        { "a type with no Apply case", [RaisedEvent(), new UnknownStoredEvent { DisputeId = DisputeId, Version = 2 }] },
    };

    private static DisputeRaised RaisedEvent() =>
        new("CUS-000001", Disputable, Reason.Unrecognised, "Not me", null, 87.23m, DisputeStatus.Submitted)
        {
            DisputeId = DisputeId,
            Version = 1,
            OccurredAtUtc = Now,
            ActorType = ActorType.Customer,
            ActorId = "login-a",
        };

    private static ReviewStarted Started(int version) =>
        new(DisputeStatus.Submitted, DisputeStatus.UnderReview) { DisputeId = DisputeId, Version = version, OccurredAtUtc = Now.AddHours(version) };

    private static DisputeEscalated Escalated(int version, DisputeStatus from) =>
        new(from, DisputeStatus.Escalated) { DisputeId = DisputeId, Version = version, OccurredAtUtc = Now.AddHours(version) };

    private static DisputeResolved Resolved(int version) =>
        new(DisputeStatus.UnderReview, DisputeStatus.Resolved, Outcome.Upheld, "Refunded") { DisputeId = DisputeId, Version = version, OccurredAtUtc = Now.AddHours(version) };

    private static DisputeWithdrawn Withdrawn(int version) =>
        new(DisputeStatus.Submitted, DisputeStatus.Withdrawn, "Found it") { DisputeId = DisputeId, Version = version, OccurredAtUtc = Now.AddHours(version) };

    /// <summary>Every public property of the dispute but its pending changes, by name.</summary>
    private static Dictionary<string, object?> StateOf(Dispute dispute) =>
        typeof(Dispute).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != nameof(Dispute.PendingChanges))
            .ToDictionary(property => property.Name, property => property.GetValue(dispute));

    /// <summary>A stored event type the Domain does not declare, so no <c>Apply</c> case names it.</summary>
    private sealed record UnknownStoredEvent : StoredEvent;
}
