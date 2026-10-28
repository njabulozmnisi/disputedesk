using System.Globalization;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Tests.Disputes;

/// <summary>FR-13 and FR-14 in the aggregate: what a raise stores, the amount and note rules, and the AD-8 check order.</summary>
public class DisputeRaiseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor CustomerA = new(ActorType.Customer, "login-a");

    private static readonly TransactionSnapshot Disputable = new(
        "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", "492181******4417",
        TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);

    private static readonly TransactionSnapshot Pending = Disputable with { State = TransactionState.Pending };
    private static readonly TransactionSnapshot Old = Disputable with { TransactionDate = Now.AddDays(-95) };

    [Fact]
    public void Raise_DisputableTransaction_IsSubmittedAtVersion1WithOneRaiseEntryByTheCustomer()
    {
        Dispute dispute = Raise(Disputable, Reason.Unrecognised, note: "Not me").Value;

        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        Assert.Equal(1, dispute.Version);
        TimelineEntry entry = Assert.Single(dispute.Timeline);
        Assert.Equal(DisputeAction.Raise, entry.Action);
        Assert.Null(entry.FromStatus);
        Assert.Equal(DisputeStatus.Submitted, entry.ToStatus);
        Assert.Equal(ActorType.Customer, entry.ActorType);
        Assert.Equal("login-a", entry.ActorId);
        Assert.Equal("Not me", entry.VisibleNote);
        Assert.Equal(dispute.Id, entry.DisputeId);
    }

    [Fact]
    public void Raise_DisputableTransaction_StampsEveryInstantWithNowAndTheSla240HoursLater()
    {
        Dispute dispute = Raise(Disputable, Reason.Unrecognised).Value;

        Assert.Equal(Now, dispute.CreatedAtUtc);
        Assert.Equal(Now.AddHours(240), dispute.SlaDueAtUtc);
        Assert.Equal(Now, Assert.Single(dispute.Timeline).OccurredAtUtc);
        Assert.Equal(Now, Assert.Single(dispute.TakePendingChanges()).OccurredAt);
        Assert.Equal(7, dispute.Id.Version);
    }

    [Fact]
    public void Raise_DisputableTransaction_StoresTheSnapshotFromTheTransaction()
    {
        Dispute dispute = Raise(Disputable, Reason.DuplicateCharge).Value;

        Assert.Equal("CUS-000001", dispute.CustomerId);
        Assert.Equal("ACC-A-1", dispute.AccountRef);
        Assert.Equal("TXN-A-0029", dispute.TransactionId);
        Assert.Equal("0011", dispute.AccountLast4);
        Assert.Equal(Disputable.TransactionDate, dispute.TransactionDateUtc);
        Assert.Equal(TimeSpan.Zero, dispute.TransactionDateUtc.Offset);
        Assert.Equal("Steers Braamfontein", dispute.Description);
        Assert.Equal(87.23m, dispute.TransactionAmount);
        Assert.Equal("ZAR", dispute.Currency);
        Assert.Equal("492181******4417", dispute.MaskedCard);
        Assert.Equal(Reason.DuplicateCharge, dispute.Reason);
    }

    [Fact]
    public void Raise_AnyReasonButWrongAmount_DisputesTheFullAmount()
    {
        Dispute dispute = Raise(Disputable, Reason.NotReceived).Value;

        Assert.Equal(87.23m, dispute.DisputedAmount);
        Assert.Null(dispute.ExpectedAmount);
    }

    [Theory]
    [InlineData("87.00", "0.23")]
    [InlineData("0.00", "87.23")]
    [InlineData("87.22", "0.01")]
    public void Raise_WrongAmountBelowTheTransactionAmount_DisputesTheDifferenceInDecimal(string expected, string disputed)
    {
        Dispute dispute = Raise(Disputable, Reason.WrongAmount, expectedAmount: Amount(expected)).Value;

        Assert.Equal(Amount(disputed), dispute.DisputedAmount);
        Assert.Equal(Amount(expected), dispute.ExpectedAmount);
    }

    [Fact]
    public void Raise_WrongAmountWithoutAnExpectedAmount_IsExpectedAmountRequired()
    {
        Assert.Equal(DisputeErrors.ExpectedAmountRequired, Raise(Disputable, Reason.WrongAmount).Error);
    }

    [Theory]
    [InlineData(Reason.Unrecognised)]
    [InlineData(Reason.DuplicateCharge)]
    [InlineData(Reason.NotReceived)]
    [InlineData(Reason.CancelledSubscription)]
    [InlineData(Reason.RefundNotProcessed)]
    public void Raise_ExpectedAmountWithAnotherReason_IsExpectedAmountNotAllowed(Reason reason)
    {
        Assert.Equal(DisputeErrors.ExpectedAmountNotAllowed, Raise(Disputable, reason, expectedAmount: 10m).Error);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("10.001")]
    public void Raise_ExpectedAmountNegativeOrWithMoreThanTwoDecimals_IsExpectedAmountMalformed(string expected)
    {
        Assert.Equal(DisputeErrors.ExpectedAmountMalformed, Raise(Disputable, Reason.WrongAmount, expectedAmount: Amount(expected)).Error);
    }

    [Theory]
    [InlineData("87.23")]
    [InlineData("87.24")]
    [InlineData("1000")]
    public void Raise_ExpectedAmountNotBelowTheTransactionAmount_IsExpectedAmountNotBelow(string expected)
    {
        Result<Dispute> result = Raise(Disputable, Reason.WrongAmount, expectedAmount: Amount(expected));

        Assert.Equal(DisputeErrors.ExpectedAmountNotBelowTransactionAmount, result.Error);
        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
    }

    [Fact]
    public void Raise_NoteWithSurroundingWhitespace_StoresItTrimmed()
    {
        Dispute dispute = Raise(Disputable, Reason.Unrecognised, note: "  \n Not me \t ").Value;

        Assert.Equal("Not me", dispute.Note);
        Assert.Equal("Not me", Assert.Single(dispute.Timeline).VisibleNote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Raise_NoteEmptyOrWhitespace_StoresNoNote(string note)
    {
        Dispute dispute = Raise(Disputable, Reason.Unrecognised, note: note).Value;

        Assert.Null(dispute.Note);
        Assert.Null(Assert.Single(dispute.Timeline).VisibleNote);
    }

    [Fact]
    public void Raise_Note1000CharactersAfterTrimming_IsAccepted()
    {
        Dispute dispute = Raise(Disputable, Reason.Unrecognised, note: "  " + new string('a', 1000) + "  ").Value;

        Assert.Equal(1000, dispute.Note?.Length);
    }

    [Fact]
    public void Raise_Note1001CharactersAfterTrimming_IsNoteTooLong()
    {
        Assert.Equal(DisputeErrors.NoteTooLong, Raise(Disputable, Reason.Unrecognised, note: new string('a', 1001)).Error);
    }

    [Fact]
    public void Raise_NoteCountedInUtf16CodeUnits_AnEmojiCountsAsTwo()
    {
        // AD-4: length is UTF-16 code units. 500 emoji are 1000 code units; one more character is over.
        string emoji = string.Concat(Enumerable.Repeat("\U0001F600", 500));

        Assert.True(Raise(Disputable, Reason.Unrecognised, note: emoji).IsSuccess);
        Assert.Equal(DisputeErrors.NoteTooLong, Raise(Disputable, Reason.Unrecognised, note: emoji + "a").Error);
    }

    [Fact]
    public void Raise_EverDisputed_IsAlreadyDisputed()
    {
        Assert.Equal(DisputeErrors.AlreadyDisputed, Raise(Disputable, Reason.Unrecognised, everDisputed: true).Error);
    }

    [Fact]
    public void Raise_EverDisputedAndPending_IsAlreadyDisputedBeforeNotDisputable()
    {
        Assert.Equal(DisputeErrors.AlreadyDisputed, Raise(Pending, Reason.Unrecognised, everDisputed: true).Error);
    }

    [Theory]
    [MemberData(nameof(NotDisputableTransactions))]
    public void Raise_NotDisputable_IsNotDisputableWithTheReason(TransactionSnapshot transaction, NotDisputableReason reason)
    {
        Error? error = Raise(transaction, Reason.Unrecognised).Error;

        Assert.NotNull(error);
        Assert.Equal(ErrorKind.BusinessRule, error.Kind);
        Assert.Equal("not-disputable", error.Code);
        Assert.Equal(reason, error.Extensions["reason"]);
    }

    public static TheoryData<TransactionSnapshot, NotDisputableReason> NotDisputableTransactions => new()
    {
        { Pending, NotDisputableReason.NotEligible },
        { Disputable with { Direction = TransactionDirection.Credit }, NotDisputableReason.NotEligible },
        { Disputable with { Kind = TransactionKind.OwnAccountTransfer }, NotDisputableReason.NotEligible },
        { Old, NotDisputableReason.OutsideWindow },
    };

    [Fact]
    public void Raise_PendingWithExpectedAmountNotBelow_IsNotDisputableBeforeTheAmountRule()
    {
        // AD-8: 422 comes before the 400 that needs the transaction amount.
        Assert.Equal("not-disputable", Raise(Pending, Reason.WrongAmount, expectedAmount: 500m).Error?.Code);
    }

    [Fact]
    public void Raise_EverDisputedWithExpectedAmountForAnotherReason_IsTheInputRuleFirst()
    {
        // AD-8: the input's own rules (400) come before any rule that needs the transaction.
        Assert.Equal(DisputeErrors.ExpectedAmountNotAllowed, Raise(Disputable, Reason.Unrecognised, everDisputed: true, expectedAmount: 1m).Error);
    }

    [Fact]
    public void TakePendingChanges_AfterRaise_ReturnsTheRaiseOnceThenNothing()
    {
        Dispute dispute = Raise(Disputable, Reason.Unrecognised).Value;

        DisputeChange change = Assert.Single(dispute.TakePendingChanges());

        Assert.Equal(new DisputeChange(DisputeAction.Raise, null, DisputeStatus.Submitted, CustomerA, Now, 1), change);
        Assert.Empty(dispute.TakePendingChanges());
    }

    private static decimal Amount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static Result<Dispute> Raise(
        TransactionSnapshot transaction, Reason reason, string? note = null, decimal? expectedAmount = null, bool everDisputed = false) =>
        Dispute.Raise("CUS-000001", transaction, everDisputed, reason, note, expectedAmount, CustomerA, Now);
}
