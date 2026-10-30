using DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// One row of the customer's history list (AD-13): the dispute without its note, expected amount or timeline, which
/// the detail carries. <c>outcome</c> is <c>upheld</c> or <c>rejected</c> once the dispute is resolved, and null until then.
/// </summary>
public sealed record DisputeSummaryResponse(
    Guid DisputeId,
    CustomerStatus CustomerStatus,
    Outcome? Outcome,
    Reason Reason,
    TransactionSnapshotResponse Transaction,
    Money DisputedAmount,
    DateTimeOffset CreatedAt);
