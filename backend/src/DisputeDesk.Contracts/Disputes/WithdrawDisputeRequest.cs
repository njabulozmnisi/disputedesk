namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// The body of <c>POST /api/v1/disputes/{disputeId}/withdraw</c> (FR-18). The note is checked after the dispute is
/// loaded, so an unknown dispute gets 404 before a note too long gets 400 (AD-8). Any other property is ignored.
/// </summary>
/// <param name="Note">
/// Optional, at most 500 characters (UTF-16 code units) after trimming. Absent, null, empty or only whitespace means no note.
/// </param>
public sealed record WithdrawDisputeRequest(string? Note = null)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and the note is free text that
    /// may hold personal data (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(WithdrawDisputeRequest);
}
