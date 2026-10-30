namespace DisputeDesk.Domain;

/// <summary>
/// An expected failure. Unexpected failures are exceptions. <see cref="Code"/> is a slug from the AD-3 catalogue.
/// The message never repeats an input value. Record equality compares the dictionaries by reference, so two errors
/// are equal only when they have no field errors or extensions and the same kind, code and message.
/// </summary>
public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    private static readonly Dictionary<string, string[]> NoFieldErrors = [];
    private static readonly Dictionary<string, object?> NoExtensions = [];

    /// <summary>For <see cref="ErrorKind.Validation"/>: the message for each field that failed, keyed by field name.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; private init; } = NoFieldErrors;

    /// <summary>Extra members of the problem body, such as the caller's view of the current status.</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; private init; } = NoExtensions;

    public static Error NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    /// <summary>A field rule failed, such as the trimmed note length. <paramref name="field"/> names the field.</summary>
    public static Error Validation(string code, string message, string field) =>
        new(ErrorKind.Validation, code, message) { FieldErrors = new Dictionary<string, string[]> { [field] = [message] } };

    public static Error Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);

    /// <summary>
    /// The action is not allowed in the current status. <paramref name="statusField"/> names the extension that
    /// carries <paramref name="currentStatus"/>: <c>customerStatus</c> for customers, <c>disputeStatus</c> for operations.
    /// </summary>
    public static Error ActionNotAllowed(string code, string message, string statusField, Enum currentStatus) =>
        new(ErrorKind.ActionNotAllowed, code, message) { Extensions = new Dictionary<string, object?> { [statusField] = currentStatus } };

    public static Error Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    /// <summary>A conflict whose problem carries <paramref name="extensions"/>, such as the existing dispute and its customer status.</summary>
    public static Error Conflict(string code, string message, IReadOnlyDictionary<string, object?> extensions) =>
        new(ErrorKind.Conflict, code, message) { Extensions = extensions };

    public static Error BusinessRule(string code, string message) => new(ErrorKind.BusinessRule, code, message);

    /// <summary>A broken rule whose problem carries <paramref name="value"/> under <paramref name="extension"/>, such as the <c>reason</c> a transaction is not disputable.</summary>
    public static Error BusinessRule(string code, string message, string extension, Enum value) =>
        new(ErrorKind.BusinessRule, code, message) { Extensions = new Dictionary<string, object?> { [extension] = value } };

    /// <summary>A broken rule whose problem carries <paramref name="extensions"/>, such as the <c>attemptsRemaining</c> of a wrong OTP.</summary>
    public static Error BusinessRule(string code, string message, IReadOnlyDictionary<string, object?> extensions) =>
        new(ErrorKind.BusinessRule, code, message) { Extensions = extensions };

    public static Error Unavailable(string code, string message) => new(ErrorKind.Unavailable, code, message);

    public static Error RateLimited(string code, string message) => new(ErrorKind.RateLimited, code, message);
}
