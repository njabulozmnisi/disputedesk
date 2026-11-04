using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Disputes;

/// <summary>The expected failures of the dispute actions. The codes are AD-3 problem slugs; messages never echo a value.</summary>
public static class DisputeErrors
{
    /// <summary>
    /// No such dispute, or not this caller's (AD-11): every dispute lookup answers the same 404. The message never echoes
    /// the ID (AD-3).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound("not-found", "There is no such dispute.");

    /// <summary>
    /// The transaction already has a dispute. A customer gets it with their dispute and its status as they see it,
    /// built in Application (AD-13).
    /// </summary>
    public static readonly Error AlreadyDisputed = Error.Conflict("already-disputed", "This transaction has already been disputed.");

    /// <summary>
    /// A version clash (AD-15): another change appended the same stored event version first, so
    /// <c>UQ_StoredEvents_DisputeId_Version</c> refused this one. The handler replays the stream and decides once more.
    /// </summary>
    public static readonly Error VersionClash =
        Error.Conflict("action-not-allowed", "The dispute was changed by another action at the same time.");

    /// <summary>
    /// The action is not allowed in the dispute's status (FR-24). Each handler returns it with the status as its caller
    /// sees it, built in Application.
    /// </summary>
    public static readonly Error ActionNotAllowed =
        new(ErrorKind.ActionNotAllowed, "action-not-allowed", "This action is not allowed in the dispute's current status.");

    public static readonly Error NoteTooLong =
        Error.Validation("validation", "The note must be at most 1000 characters after trimming.", "Note");

    public static readonly Error WithdrawNoteTooLong =
        Error.Validation("validation", "The note must be at most 500 characters after trimming.", "Note");

    public static readonly Error ResolveNoteInvalid =
        Error.Validation("validation", "A note of 1 to 500 characters after trimming is required.", "Note");

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
        Error.BusinessRule("not-disputable", "This transaction cannot be disputed.", new Dictionary<string, object?> { ["reason"] = reason });
}
