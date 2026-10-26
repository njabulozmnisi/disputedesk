namespace DisputeDesk.Domain;

/// <summary>An expected failure. Unexpected failures are exceptions. <see cref="Code"/> is a slug from the AD-3 catalogue.</summary>
public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    public static Error NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static Error Validation(string code, string message) => new(ErrorKind.Validation, code, message);

    public static Error Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);

    public static Error ActionNotAllowed(string code, string message) => new(ErrorKind.ActionNotAllowed, code, message);

    public static Error Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static Error BusinessRule(string code, string message) => new(ErrorKind.BusinessRule, code, message);

    public static Error Unavailable(string code, string message) => new(ErrorKind.Unavailable, code, message);
}
