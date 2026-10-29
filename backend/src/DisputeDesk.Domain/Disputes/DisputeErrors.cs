using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Disputes;

/// <summary>The expected failures of the dispute actions. The codes are AD-3 problem slugs; messages never echo a value.</summary>
public static class DisputeErrors
{
    public static readonly Error AlreadyDisputed = Error.Conflict("already-disputed", "This transaction has already been disputed.");

    /// <summary>
    /// <see cref="AlreadyDisputed"/> for a customer: the problem names their dispute and its status as they see it
    /// (AD-13), <paramref name="customerStatus"/>, which Application maps.
    /// </summary>
    public static Error AlreadyDisputedAs(Guid disputeId, Enum customerStatus) => Error.Conflict(
        "already-disputed",
        AlreadyDisputed.Message,
        new Dictionary<string, object?> { ["disputeId"] = disputeId, ["customerStatus"] = customerStatus });

    /// <summary>
    /// A version clash (AD-15): another change appended the same stored event version first, so
    /// <c>UQ_StoredEvents_DisputeId_Version</c> refused this one. The handler replays the stream and decides once more.
    /// </summary>
    public static readonly Error VersionClash =
        Error.Conflict("action-not-allowed", "The dispute was changed by another action at the same time.");

    public static readonly Error NoteTooLong =
        Error.Validation("validation", "The note must be at most 1000 characters after trimming.", "Note");

    public static readonly Error ExpectedAmountRequired =
        Error.Validation("validation", "An expected amount is required for the reason wrong amount.", "ExpectedAmount");

    public static readonly Error ExpectedAmountNotAllowed =
        Error.Validation("validation", "An expected amount is allowed only for the reason wrong amount.", "ExpectedAmount");

    public static readonly Error ExpectedAmountMalformed =
        Error.Validation("validation", "The expected amount must be 0.00 or more, with at most 2 decimals.", "ExpectedAmount");

    public static readonly Error ExpectedAmountNotBelowTransactionAmount =
        Error.Validation("validation", "The expected amount must be less than the transaction amount.", "ExpectedAmount");

    /// <summary>The transaction cannot be disputed for <paramref name="reason"/>, which the problem carries as <c>reason</c>.</summary>
    public static Error NotDisputable(NotDisputableReason reason) =>
        Error.BusinessRule("not-disputable", "This transaction cannot be disputed.", "reason", reason);
}
