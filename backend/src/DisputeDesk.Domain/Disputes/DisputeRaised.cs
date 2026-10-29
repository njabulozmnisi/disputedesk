namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// The dispute was raised: everything needed to rebuild it (AD-13). Private, because it holds the customer ID, the
/// account reference and the transaction snapshot (ad-009). There is no from-status.
/// </summary>
public sealed record DisputeRaised(
    string CustomerId,
    TransactionSnapshot Transaction,
    Reason Reason,
    string? Note,
    decimal? ExpectedAmount,
    decimal DisputedAmount,
    DisputeStatus ToStatus) : StoredEvent;
