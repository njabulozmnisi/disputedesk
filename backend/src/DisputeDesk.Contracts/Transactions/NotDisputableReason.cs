namespace DisputeDesk.Contracts.Transactions;

/// <summary>Why a transaction row is not disputable (AD-13).</summary>
public enum NotDisputableReason
{
    /// <summary>Not a settled card debit: pending, a credit or a transfer.</summary>
    NotEligible,

    /// <summary>Dated more than 90 South African calendar days ago.</summary>
    OutsideWindow,
}
