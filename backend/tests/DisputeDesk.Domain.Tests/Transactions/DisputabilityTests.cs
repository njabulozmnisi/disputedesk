using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Tests.Transactions;

/// <summary>FR-12: only a settled card debit inside the 90-day window is disputable; the window ends with D + 90 in South African time.</summary>
public class DisputabilityTests
{
    private static readonly TimeSpan SouthAfricanOffset = TimeSpan.FromHours(2);

    // D is 1 March 2026 in South Africa. 23:30 SAST is still 1 March there, but already 21:30 UTC on the same day;
    // 00:30 SAST on 1 March is 22:30 UTC on 28 February, which a UTC calendar would count as the day before.
    private static readonly DateTimeOffset LateOnD = new(2026, 3, 1, 23, 30, 0, SouthAfricanOffset);
    private static readonly DateTimeOffset EarlyOnD = new(2026, 3, 1, 0, 30, 0, SouthAfricanOffset);

    // D + 90 is 30 May 2026; its last instant is just before midnight South African time, 21:59:59.9999999 UTC.
    private static readonly DateTimeOffset FirstInstantAfterDPlus90 = new(2026, 5, 31, 0, 0, 0, SouthAfricanOffset);
    private static readonly DateTimeOffset LastInstantOfDPlus90 = FirstInstantAfterDPlus90.AddTicks(-1);

    [Fact]
    public void ReasonNotDisputable_SettledCardDebitOnTheLastInstantOfDPlus90_IsDisputable()
    {
        NotDisputableReason? reason = SettledCardDebit(LateOnD, LastInstantOfDPlus90);

        Assert.Null(reason);
    }

    [Fact]
    public void ReasonNotDisputable_SettledCardDebitOnTheFirstInstantAfterDPlus90_IsOutsideWindow()
    {
        NotDisputableReason? reason = SettledCardDebit(LateOnD, FirstInstantAfterDPlus90);

        Assert.Equal(NotDisputableReason.OutsideWindow, reason);
    }

    [Fact]
    public void ReasonNotDisputable_DatedEarlyOnDInSouthAfrica_CountsFromThatSouthAfricanDay()
    {
        // Counted from the UTC date (28 February) the window would already have closed at this instant.
        Assert.Null(SettledCardDebit(EarlyOnD, LastInstantOfDPlus90));
        Assert.Equal(NotDisputableReason.OutsideWindow, SettledCardDebit(EarlyOnD, FirstInstantAfterDPlus90));
    }

    [Fact]
    public void ReasonNotDisputable_TransactionDateInUtc_GivesTheSameAnswerAsInSouthAfricanTime()
    {
        DateTimeOffset earlyOnDInUtc = EarlyOnD.ToUniversalTime();

        Assert.Null(SettledCardDebit(earlyOnDInUtc, LastInstantOfDPlus90.ToUniversalTime()));
        Assert.Equal(NotDisputableReason.OutsideWindow, SettledCardDebit(earlyOnDInUtc, FirstInstantAfterDPlus90.ToUniversalTime()));
    }

    [Fact]
    public void ReasonNotDisputable_SettledCardDebitOnTheSameDay_IsDisputable()
    {
        Assert.Null(SettledCardDebit(LateOnD, LateOnD.AddMinutes(5)));
    }

    [Theory]
    [InlineData(TransactionDirection.Debit, TransactionKind.Card, TransactionState.Pending)]
    [InlineData(TransactionDirection.Credit, TransactionKind.Card, TransactionState.Settled)]
    [InlineData(TransactionDirection.Debit, TransactionKind.OwnAccountTransfer, TransactionState.Settled)]
    [InlineData(TransactionDirection.Debit, TransactionKind.Transfer, TransactionState.Settled)]
    [InlineData(TransactionDirection.Debit, TransactionKind.Other, TransactionState.Settled)]
    [InlineData(TransactionDirection.Credit, TransactionKind.Transfer, TransactionState.Settled)]
    public void ReasonNotDisputable_AnythingButASettledCardDebit_IsNotEligible(
        TransactionDirection direction, TransactionKind kind, TransactionState state)
    {
        NotDisputableReason? reason = Disputability.ReasonNotDisputable(everDisputed: false, direction, kind, state, LateOnD, LastInstantOfDPlus90);

        Assert.Equal(NotDisputableReason.NotEligible, reason);
    }

    [Fact]
    public void ReasonNotDisputable_NotEligibleAndOutsideWindow_IsNotEligible()
    {
        // Eligibility never changes; the window does. So a credit stays "not eligible" after the window closes.
        NotDisputableReason? reason = Disputability.ReasonNotDisputable(
            everDisputed: false, TransactionDirection.Credit, TransactionKind.Transfer, TransactionState.Settled, LateOnD, FirstInstantAfterDPlus90);

        Assert.Equal(NotDisputableReason.NotEligible, reason);
    }

    [Fact]
    public void ReasonNotDisputable_EverDisputedSettledCardDebitInsideTheWindow_IsAlreadyDisputed()
    {
        NotDisputableReason? reason = Disputability.ReasonNotDisputable(
            everDisputed: true, TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled, LateOnD, LateOnD.AddMinutes(5));

        Assert.Equal(NotDisputableReason.AlreadyDisputed, reason);
    }

    [Fact]
    public void ReasonNotDisputable_EverDisputedAndOutsideWindowAndNotEligible_IsAlreadyDisputed()
    {
        // AD-13's raise order: an earlier dispute (409) is reported before eligibility and the window (422).
        NotDisputableReason? reason = Disputability.ReasonNotDisputable(
            everDisputed: true, TransactionDirection.Debit, TransactionKind.Card, TransactionState.Pending, LateOnD, FirstInstantAfterDPlus90);

        Assert.Equal(NotDisputableReason.AlreadyDisputed, reason);
    }

    private static NotDisputableReason? SettledCardDebit(DateTimeOffset transactionDate, DateTimeOffset now) =>
        Disputability.ReasonNotDisputable(everDisputed: false, TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled, transactionDate, now);
}
