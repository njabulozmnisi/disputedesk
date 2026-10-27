namespace DisputeDesk.Domain.Transactions;

/// <summary>Why a transaction cannot be disputed (FR-12). The disputes epic adds "already disputed".</summary>
public enum NotDisputableReason
{
    /// <summary>Only a settled card debit can be disputed: not a pending transaction, a credit or a transfer.</summary>
    NotEligible,

    /// <summary>The 90 South African calendar days after the transaction date have passed.</summary>
    OutsideWindow,
}
