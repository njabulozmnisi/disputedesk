namespace DisputeDesk.Domain;

/// <summary>
/// The kind of an expected failure (AD-3). <c>ErrorResults.ToProblem</c> in the Api maps each kind to one HTTP status.
/// </summary>
public enum ErrorKind
{
    /// <summary>The thing does not exist, or it is not the caller's (404).</summary>
    NotFound,

    /// <summary>The input breaks a field rule, such as the expected amount or the trimmed note length (400).</summary>
    Validation,

    /// <summary>The caller is known but may not do this (403).</summary>
    Forbidden,

    /// <summary>The action is not allowed in the current status (409).</summary>
    ActionNotAllowed,

    /// <summary>The request clashes with the current state of another resource, such as a duplicate (409).</summary>
    Conflict,

    /// <summary>The request is well-formed but fails a rule check (422).</summary>
    BusinessRule,

    /// <summary>A dependency the request needs is unavailable (503).</summary>
    Unavailable,
}
