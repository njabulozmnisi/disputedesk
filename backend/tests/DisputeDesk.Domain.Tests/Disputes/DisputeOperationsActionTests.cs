using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Tests.Disputes;

/// <summary>
/// FR-21 to FR-24: <see cref="Dispute.StartReview"/>, <see cref="Dispute.Escalate"/> and <see cref="Dispute.Resolve"/>
/// called on a dispute in each of the five statuses (15 cells, NFR-TEST-2), and the resolve note's rule, checked before
/// the status (AD-8).
/// </summary>
public class DisputeOperationsActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(30);
    private static readonly Actor OpsA = new(ActorType.Operations, "ops-a");
    private static readonly Guid DisputeId = Guid.CreateVersion7(Now);

    private static readonly TransactionSnapshot Disputable = new(
        "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", null,
        TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);

    /// <summary>FR-24, every cell of the three operations actions: the status it moves to, or null where it is refused.</summary>
    public static TheoryData<DisputeStatus, DisputeAction, DisputeStatus?> Cells => new()
    {
        { DisputeStatus.Submitted, DisputeAction.StartReview, DisputeStatus.UnderReview },
        { DisputeStatus.UnderReview, DisputeAction.StartReview, null },
        { DisputeStatus.Escalated, DisputeAction.StartReview, null },
        { DisputeStatus.Resolved, DisputeAction.StartReview, null },
        { DisputeStatus.Withdrawn, DisputeAction.StartReview, null },
        { DisputeStatus.Submitted, DisputeAction.Escalate, DisputeStatus.Escalated },
        { DisputeStatus.UnderReview, DisputeAction.Escalate, DisputeStatus.Escalated },
        { DisputeStatus.Escalated, DisputeAction.Escalate, null },
        { DisputeStatus.Resolved, DisputeAction.Escalate, null },
        { DisputeStatus.Withdrawn, DisputeAction.Escalate, null },
        { DisputeStatus.Submitted, DisputeAction.Resolve, null },
        { DisputeStatus.UnderReview, DisputeAction.Resolve, DisputeStatus.Resolved },
        { DisputeStatus.Escalated, DisputeAction.Resolve, DisputeStatus.Resolved },
        { DisputeStatus.Resolved, DisputeAction.Resolve, null },
        { DisputeStatus.Withdrawn, DisputeAction.Resolve, null },
    };

    [Theory]
    [MemberData(nameof(Cells))]
    public void Action_InEachStatus_SucceedsOnlyInItsFr24CellsWithOneStoredEvent(DisputeStatus from, DisputeAction action, DisputeStatus? to)
    {
        Dispute dispute = DisputeIn(from);
        int version = dispute.Version;

        Result result = Act(dispute, action);

        if (to is not DisputeStatus toStatus)
        {
            Assert.Equal(DisputeErrors.ActionNotAllowed, result.Error);
            Assert.Equal(from, dispute.Status);
            Assert.Equal(version, dispute.Version);
            Assert.Empty(dispute.PendingChanges);
            return;
        }

        Assert.True(result.IsSuccess);
        Assert.Equal(toStatus, dispute.Status);
        Assert.Equal(version + 1, dispute.Version);
        StoredEvent appended = Assert.Single(dispute.PendingChanges);
        Assert.Equal(DisputeId, appended.DisputeId);
        Assert.Equal(version + 1, appended.Version);
        Assert.Equal(Later, appended.OccurredAtUtc);
        Assert.Equal(ActorType.Operations, appended.ActorType);
        Assert.Equal("ops-a", appended.ActorId);
        (DisputeStatus eventFrom, DisputeStatus eventTo) = appended switch
        {
            ReviewStarted started when action == DisputeAction.StartReview => (started.FromStatus, started.ToStatus),
            DisputeEscalated escalated when action == DisputeAction.Escalate => (escalated.FromStatus, escalated.ToStatus),
            DisputeResolved resolved when action == DisputeAction.Resolve => (resolved.FromStatus, resolved.ToStatus),
            _ => throw new InvalidOperationException($"{appended} is not the stored event of {action}."),
        };
        Assert.Equal(from, eventFrom);
        Assert.Equal(toStatus, eventTo);
    }

    [Fact]
    public void Resolve_Valid_CarriesTheOutcomeAndTheTrimmedNoteAndSetsTheOutcome()
    {
        Dispute dispute = DisputeIn(DisputeStatus.UnderReview);

        Result result = dispute.Resolve(Outcome.Rejected, "  The merchant proved delivery.  ", OpsA, Later);

        Assert.True(result.IsSuccess);
        DisputeResolved resolved = Assert.IsType<DisputeResolved>(Assert.Single(dispute.PendingChanges));
        Assert.Equal(Outcome.Rejected, resolved.Outcome);
        Assert.Equal("The merchant proved delivery.", resolved.Note);
        Assert.Equal(Outcome.Rejected, dispute.Outcome);
    }

    public static TheoryData<DisputeStatus> UnderReviewAndResolved => [DisputeStatus.UnderReview, DisputeStatus.Resolved];

    [Theory]
    [MemberData(nameof(UnderReviewAndResolved))]
    public void Resolve_NoteNullWhitespaceOrOver500UnitsAfterTrimming_ReturnsTheNoteErrorBeforeTheStatus(DisputeStatus from)
    {
        string?[] badNotes =
        [
            null,
            "",
            " \t\n ",
            new string('a', 501),
            "  " + new string('a', 501) + "  ",
            new string('a', 499) + "\U0001F600", // a surrogate pair straddling the limit: 501 units
        ];

        foreach (string? note in badNotes)
        {
            Dispute dispute = DisputeIn(from);

            Result result = dispute.Resolve(Outcome.Upheld, note, OpsA, Later);

            Error error = Assert.IsType<Error>(result.Error);
            Assert.Equal(DisputeErrors.ResolveNoteInvalid, error);
            Assert.Equal(ErrorKind.Validation, error.Kind);
            Assert.Equal(["Note"], error.FieldErrors.Keys);
            Assert.Contains("500", error.Message, StringComparison.Ordinal);
            Assert.Equal(from, dispute.Status);
            Assert.Empty(dispute.PendingChanges);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)] // 500 units padded with spaces on both sides
    public void Resolve_NoteOf500UnitsAfterTrimmingOnUnderReview_SucceedsWithTheTrimmedNote(int padding)
    {
        string padded = new string(' ', padding) + new string('a', 500) + new string(' ', padding);
        Dispute dispute = DisputeIn(DisputeStatus.UnderReview);

        Result result = dispute.Resolve(Outcome.Upheld, padded, OpsA, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(new string('a', 500), Assert.IsType<DisputeResolved>(Assert.Single(dispute.PendingChanges)).Note);
    }

    [Fact]
    public void Resolve_SurrogatePairEndingAtTheLimitOnUnderReview_Succeeds()
    {
        string note = new string('a', 498) + "\U0001F600";
        Dispute dispute = DisputeIn(DisputeStatus.UnderReview);

        Result result = dispute.Resolve(Outcome.Upheld, note, OpsA, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(note, Assert.IsType<DisputeResolved>(Assert.Single(dispute.PendingChanges)).Note);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Resolve_NoteOf500UnitsOnResolved_ReturnsActionNotAllowed(int padding)
    {
        string padded = new string(' ', padding) + new string('a', 500) + new string(' ', padding);
        Dispute dispute = DisputeIn(DisputeStatus.Resolved);

        Result result = dispute.Resolve(Outcome.Upheld, padded, OpsA, Later);

        Assert.Equal(DisputeErrors.ActionNotAllowed, result.Error);
        Assert.Empty(dispute.PendingChanges);
    }

    [Fact]
    public void ResolveNoteMaxLength_IsTheWithdrawLimit()
    {
        Assert.Equal(500, Dispute.ResolveNoteMaxLength);
    }

    [Fact]
    public void ActionNotAllowedWithDisputeStatus_CarriesTheStatusUnderDisputeStatus()
    {
        Error error = DisputeErrors.ActionNotAllowedWithDisputeStatus(DisputeStatus.Escalated);

        Assert.Equal(ErrorKind.ActionNotAllowed, error.Kind);
        Assert.Equal("action-not-allowed", error.Code);
        Assert.Equal(["disputeStatus"], error.Extensions.Keys);
        Assert.Equal(DisputeStatus.Escalated, error.Extensions["disputeStatus"]);
    }

    [Fact]
    public void StartReviewEscalateAndResolve_ThenReplayOfTheWholeStream_GivesTheSameDispute()
    {
        DisputeRaised raised = RaisedEvent();
        var dispute = Dispute.Replay([raised]);
        Assert.True(dispute.StartReview(OpsA, Later).IsSuccess);
        Assert.True(dispute.Escalate(OpsA, Later).IsSuccess);
        Assert.True(dispute.Resolve(Outcome.Upheld, "We've reversed this debit.", OpsA, Later).IsSuccess);

        var replayed = Dispute.Replay([raised, .. dispute.PendingChanges]);

        Assert.Equal(DisputeStatus.Resolved, replayed.Status);
        Assert.Equal(Outcome.Upheld, replayed.Outcome);
        Assert.Equal(4, replayed.Version);
        Assert.Equal("Not me", replayed.Note); // the raise's note, not the resolve's
    }

    private static Result Act(Dispute dispute, DisputeAction action) => action switch
    {
        DisputeAction.StartReview => dispute.StartReview(OpsA, Later),
        DisputeAction.Escalate => dispute.Escalate(OpsA, Later),
        DisputeAction.Resolve => dispute.Resolve(Outcome.Upheld, "We've reversed this debit.", OpsA, Later),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not an operations action."),
    };

    /// <summary>A dispute whose stream reaches <paramref name="status"/>, through the stored events that lead there.</summary>
    private static Dispute DisputeIn(DisputeStatus status) => Dispute.Replay(status switch
    {
        DisputeStatus.Submitted => [RaisedEvent()],
        DisputeStatus.UnderReview => [RaisedEvent(), Started()],
        DisputeStatus.Escalated => [RaisedEvent(), new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated) { DisputeId = DisputeId, Version = 2, OccurredAtUtc = Now }],
        DisputeStatus.Resolved => [RaisedEvent(), Started(), new DisputeResolved(DisputeStatus.UnderReview, DisputeStatus.Resolved, Outcome.Upheld, "Refunded") { DisputeId = DisputeId, Version = 3, OccurredAtUtc = Now }],
        DisputeStatus.Withdrawn => [RaisedEvent(), new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null) { DisputeId = DisputeId, Version = 2, OccurredAtUtc = Now }],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
    });

    private static DisputeRaised RaisedEvent() =>
        new("CUS-000001", Disputable, Reason.Unrecognised, "Not me", null, 87.23m, DisputeStatus.Submitted)
        {
            DisputeId = DisputeId,
            Version = 1,
            OccurredAtUtc = Now,
            ActorType = ActorType.Customer,
            ActorId = "login-a",
        };

    private static ReviewStarted Started() =>
        new(DisputeStatus.Submitted, DisputeStatus.UnderReview) { DisputeId = DisputeId, Version = 2, OccurredAtUtc = Now };
}
