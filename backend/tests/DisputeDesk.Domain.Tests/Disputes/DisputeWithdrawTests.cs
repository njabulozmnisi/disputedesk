using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Tests.Disputes;

/// <summary>
/// FR-18 to FR-20 and FR-24: <see cref="Dispute.Withdraw"/> from each status, its note rule and its check order (the
/// note before the status, AD-8), and every cell of the allowed-actions table (NFR-TEST-2).
/// </summary>
public class DisputeWithdrawTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(30);
    private static readonly Actor CustomerA = new(ActorType.Customer, "login-a");
    private static readonly Guid DisputeId = Guid.CreateVersion7(Now);

    private static readonly TransactionSnapshot Disputable = new(
        "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", null,
        TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);

    public static TheoryData<DisputeStatus> OpenStatuses => [DisputeStatus.Submitted, DisputeStatus.UnderReview, DisputeStatus.Escalated];

    public static TheoryData<DisputeStatus> ClosedStatuses => [DisputeStatus.Resolved, DisputeStatus.Withdrawn];

    [Theory]
    [MemberData(nameof(OpenStatuses))]
    public void Withdraw_FromAnOpenStatus_AppendsOneDisputeWithdrawnAtTheNextVersion(DisputeStatus from)
    {
        Dispute dispute = DisputeIn(from);
        int version = dispute.Version;

        Result result = dispute.Withdraw("  Found it in my statement  ", CustomerA, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(DisputeStatus.Withdrawn, dispute.Status);
        Assert.Equal(version + 1, dispute.Version);
        DisputeWithdrawn withdrawn = Assert.IsType<DisputeWithdrawn>(Assert.Single(dispute.PendingChanges));
        Assert.Equal(from, withdrawn.FromStatus);
        Assert.Equal(DisputeStatus.Withdrawn, withdrawn.ToStatus);
        Assert.Equal("Found it in my statement", withdrawn.Note);
        Assert.Equal(DisputeId, withdrawn.DisputeId);
        Assert.Equal(version + 1, withdrawn.Version);
        Assert.Equal(Later, withdrawn.OccurredAtUtc);
        Assert.Equal(ActorType.Customer, withdrawn.ActorType);
        Assert.Equal("login-a", withdrawn.ActorId);
    }

    [Theory]
    [MemberData(nameof(ClosedStatuses))]
    public void Withdraw_FromAClosedStatus_ReturnsActionNotAllowedAndChangesNothing(DisputeStatus from)
    {
        Dispute dispute = DisputeIn(from);
        int version = dispute.Version;

        Result result = dispute.Withdraw("Found it", CustomerA, Later);

        Assert.Equal(DisputeErrors.ActionNotAllowed, result.Error);
        Assert.Equal(from, dispute.Status);
        Assert.Equal(version, dispute.Version);
        Assert.Empty(dispute.PendingChanges);
    }

    [Fact]
    public void Withdraw_Twice_TheSecondReturnsActionNotAllowed()
    {
        Dispute dispute = DisputeIn(DisputeStatus.Submitted);
        Assert.True(dispute.Withdraw(note: null, CustomerA, Later).IsSuccess);

        Result second = dispute.Withdraw(note: null, CustomerA, Later);

        Assert.Equal(DisputeErrors.ActionNotAllowed, second.Error);
        Assert.Single(dispute.PendingChanges);
    }

    [Theory]
    [InlineData(500, 0)]
    [InlineData(500, 3)] // 500 units padded with spaces on both sides
    public void Withdraw_NoteOf500UnitsAfterTrimming_Succeeds(int length, int padding)
    {
        string note = new string(' ', padding) + new string('a', length) + new string(' ', padding);
        Dispute dispute = DisputeIn(DisputeStatus.Submitted);

        Result result = dispute.Withdraw(note, CustomerA, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(new string('a', 500), Assert.IsType<DisputeWithdrawn>(Assert.Single(dispute.PendingChanges)).Note);
    }

    [Fact]
    public void Withdraw_NoteOf501UnitsAfterTrimming_ReturnsTheWithdrawNoteErrorAndChangesNothing()
    {
        Dispute dispute = DisputeIn(DisputeStatus.Submitted);

        Result result = dispute.Withdraw(" " + new string('a', 501) + " ", CustomerA, Later);

        Error error = Assert.IsType<Error>(result.Error);
        Assert.Equal(DisputeErrors.WithdrawNoteTooLong, error);
        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal(["Note"], error.FieldErrors.Keys);
        Assert.Contains("500", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(DisputeErrors.NoteTooLong, error);
        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        Assert.Empty(dispute.PendingChanges);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Withdraw_NoNoteOrOnlyWhitespace_RecordsNoNote(string? note)
    {
        Dispute dispute = DisputeIn(DisputeStatus.Submitted);

        Result result = dispute.Withdraw(note, CustomerA, Later);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.IsType<DisputeWithdrawn>(Assert.Single(dispute.PendingChanges)).Note);
    }

    [Theory]
    [MemberData(nameof(ClosedStatuses))]
    public void Withdraw_NoteTooLongOnAClosedDispute_ReturnsTheNoteErrorBeforeTheStatus(DisputeStatus from)
    {
        // AD-8: 400 before 409.
        Dispute dispute = DisputeIn(from);

        Result result = dispute.Withdraw(new string('a', 501), CustomerA, Later);

        Assert.Equal(DisputeErrors.WithdrawNoteTooLong, result.Error);
    }

    [Fact]
    public void Withdraw_ThenReplayOfTheWholeStream_GivesTheSameDispute()
    {
        DisputeRaised raised = RaisedEvent();
        var dispute = Dispute.Replay([raised]);
        Assert.True(dispute.Withdraw("Found it", CustomerA, Later).IsSuccess);

        var replayed = Dispute.Replay([raised, .. dispute.PendingChanges]);

        Assert.Equal(dispute.Status, replayed.Status);
        Assert.Equal(dispute.Version, replayed.Version);
        Assert.Equal(dispute.Note, replayed.Note);
        Assert.Equal("Not me", replayed.Note); // the raise's note, not the withdraw's
    }

    [Fact]
    public void ActionNotAllowedAs_CustomerStatus_CarriesItUnderCustomerStatus()
    {
        Error error = DisputeErrors.ActionNotAllowedAs(DisputeStatus.Withdrawn);

        Assert.Equal(ErrorKind.ActionNotAllowed, error.Kind);
        Assert.Equal("action-not-allowed", error.Code);
        Assert.Equal(DisputeStatus.Withdrawn, error.Extensions["customerStatus"]);
    }

    /// <summary>FR-24, every cell: the action each status allows.</summary>
    public static TheoryData<DisputeStatus, DisputeAction, bool> AllowedActionCells => new()
    {
        { DisputeStatus.Submitted, DisputeAction.StartReview, true },
        { DisputeStatus.Submitted, DisputeAction.Escalate, true },
        { DisputeStatus.Submitted, DisputeAction.Resolve, false },
        { DisputeStatus.Submitted, DisputeAction.Withdraw, true },
        { DisputeStatus.UnderReview, DisputeAction.StartReview, false },
        { DisputeStatus.UnderReview, DisputeAction.Escalate, true },
        { DisputeStatus.UnderReview, DisputeAction.Resolve, true },
        { DisputeStatus.UnderReview, DisputeAction.Withdraw, true },
        { DisputeStatus.Escalated, DisputeAction.StartReview, false },
        { DisputeStatus.Escalated, DisputeAction.Escalate, false },
        { DisputeStatus.Escalated, DisputeAction.Resolve, true },
        { DisputeStatus.Escalated, DisputeAction.Withdraw, true },
        { DisputeStatus.Resolved, DisputeAction.StartReview, false },
        { DisputeStatus.Resolved, DisputeAction.Escalate, false },
        { DisputeStatus.Resolved, DisputeAction.Resolve, false },
        { DisputeStatus.Resolved, DisputeAction.Withdraw, false },
        { DisputeStatus.Withdrawn, DisputeAction.StartReview, false },
        { DisputeStatus.Withdrawn, DisputeAction.Escalate, false },
        { DisputeStatus.Withdrawn, DisputeAction.Resolve, false },
        { DisputeStatus.Withdrawn, DisputeAction.Withdraw, false },
    };

    [Theory]
    [MemberData(nameof(AllowedActionCells))]
    public void IsAllowed_EachCellOfTheTable_MatchesFr24(DisputeStatus status, DisputeAction action, bool expected)
    {
        Assert.Equal(expected, Dispute.IsAllowed(status, action));
    }

    [Theory]
    [MemberData(nameof(OpenStatuses))]
    [MemberData(nameof(ClosedStatuses))]
    public void IsAllowed_Raise_IsNeverAnActionOnAnExistingDispute(DisputeStatus status)
    {
        Assert.False(Dispute.IsAllowed(status, DisputeAction.Raise));
    }

    /// <summary>A dispute whose stream reaches <paramref name="status"/>, through the stored events that lead there.</summary>
    private static Dispute DisputeIn(DisputeStatus status) => Dispute.Replay(status switch
    {
        DisputeStatus.Submitted => [RaisedEvent()],
        DisputeStatus.UnderReview => [RaisedEvent(), Started()],
        DisputeStatus.Escalated => [RaisedEvent(), Started(), new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated) { DisputeId = DisputeId, Version = 3, OccurredAtUtc = Now }],
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
