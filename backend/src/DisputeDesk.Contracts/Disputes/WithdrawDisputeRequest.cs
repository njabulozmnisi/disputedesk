using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// The body of <c>POST /api/v1/disputes/{disputeId}/withdraw</c> (FR-18). Built-in minimal API validation checks the note
/// before the dispute is looked up, so a note too long gets 400 whether or not the dispute exists (AD-8). Any other
/// property is ignored.
/// </summary>
/// <param name="Note">
/// Optional, at most 500 characters (UTF-16 code units) after trimming. Absent, null, empty or only whitespace means no note.
/// </param>
public sealed record WithdrawDisputeRequest(string? Note = null) : IValidatableObject
{
    /// <summary>The same limit as <c>Dispute.WithdrawNoteMaxLength</c>; Contracts uses Domain only for the enums identical on the wire (AD-1).</summary>
    public const int NoteMaxLength = 500;

    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and the note is free text that
    /// may hold personal data (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(WithdrawDisputeRequest);

    /// <summary>The note's length after trimming (AD-4), which no attribute expresses.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Note is not null && Note.Trim().Length > NoteMaxLength)
        {
            yield return new ValidationResult("The note must be at most 500 characters after trimming.", [nameof(Note)]);
        }
    }
}
