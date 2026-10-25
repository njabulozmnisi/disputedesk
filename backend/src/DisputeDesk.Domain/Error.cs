namespace DisputeDesk.Domain;

/// <summary>The kind of an expected failure. The Api maps each kind to one HTTP status.</summary>
public enum ErrorKind
{
    /// <summary>The thing does not exist, or it is not the caller's (404).</summary>
    NotFound,

    /// <summary>The requested action is not allowed from the current status (409).</summary>
    InvalidTransition,

    /// <summary>The input is malformed (422).</summary>
    Validation,

    /// <summary>The input is well-formed but breaks a business rule (422).</summary>
    BusinessRule,
}

/// <summary>An expected failure. Unexpected failures are exceptions.</summary>
public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    public static Error NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static Error InvalidTransition(string code, string message) => new(ErrorKind.InvalidTransition, code, message);

    public static Error Validation(string code, string message) => new(ErrorKind.Validation, code, message);

    public static Error BusinessRule(string code, string message) => new(ErrorKind.BusinessRule, code, message);
}
