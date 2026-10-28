namespace DisputeDesk.Domain.Transactions;

/// <summary>Why a transaction cannot be disputed (FR-12, AD-13).</summary>
public enum NotDisputableReason
{
    /// <summary>The transaction has a dispute, in any status: one dispute per transaction, ever.</summary>
    AlreadyDisputed,

    /// <summary>Only a settled card debit can be disputed: not a pending transaction, a credit or a transfer.</summary>
    NotEligible,

    /// <summary>The 90 South African calendar days after the transaction date have passed.</summary>
    OutsideWindow,
}
