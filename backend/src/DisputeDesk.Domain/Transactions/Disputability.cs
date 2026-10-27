namespace DisputeDesk.Domain.Transactions;

/// <summary>
/// Whether a transaction can be disputed (FR-12): a settled card debit made within the last 90 days. A transaction
/// dated D in South African time stays disputable until the end of day D + 90 in South African time.
/// </summary>
public static class Disputability
{
    public const int WindowDays = 90;

    // South Africa has no daylight saving time, so its offset is always UTC+02:00 (AD-4).
    private static readonly TimeSpan SouthAfricanOffset = TimeSpan.FromHours(2);

    /// <summary>The reason the transaction is not disputable at <paramref name="now"/>, or null when it is disputable.</summary>
    public static NotDisputableReason? ReasonNotDisputable(
        TransactionDirection direction,
        TransactionKind kind,
        TransactionState state,
        DateTimeOffset transactionDate,
        DateTimeOffset now)
    {
        // Checked first: eligibility never changes, while the window closes with time.
        bool isSettledCardDebit = direction == TransactionDirection.Debit
            && kind == TransactionKind.Card
            && state == TransactionState.Settled;
        if (!isSettledCardDebit)
        {
            return NotDisputableReason.NotEligible;
        }

        DateTime transactionDay = transactionDate.ToOffset(SouthAfricanOffset).Date;
        var windowClosesAt = new DateTimeOffset(transactionDay.AddDays(WindowDays + 1), SouthAfricanOffset);
        return now < windowClosesAt ? null : NotDisputableReason.OutsideWindow;
    }
}
