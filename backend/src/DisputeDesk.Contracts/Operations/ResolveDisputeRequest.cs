using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Json;

namespace DisputeDesk.Contracts.Operations;

/// <summary>
/// The body of <c>POST /api/v1/operations/disputes/{disputeId}/resolve</c> (FR-23). Built-in minimal API validation checks
/// both fields before the dispute is looked up, so a missing outcome or a bad note gets 400 whether or not the dispute
/// exists (AD-8). Any other property is ignored.
/// </summary>
/// <param name="Outcome">
/// Required: <c>upheld</c> or <c>rejected</c>, exactly; any other spelling, <c>Upheld</c> included, fails to bind.
/// </param>
/// <param name="Note">
/// Required: the note meant for the customer, 1 to 500 characters (UTF-16 code units) after trimming; stored trimmed.
/// </param>
// Outcome stays nullable: a missing outcome would otherwise bind as Upheld, the enum's first member, and pass [Required].
// [Required] refuses a note that is null, empty or only whitespace, so the lower bound of 1 after trimming needs no rule.
public sealed record ResolveDisputeRequest(
    [property: Required(ErrorMessage = "An outcome is required: upheld or rejected."), JsonConverter(typeof(CamelCaseNameOnlyEnumConverter<Outcome>))] Outcome? Outcome,
    [property: Required(ErrorMessage = ResolveDisputeRequest.NoteInvalid)] string? Note) : IValidatableObject
{
    /// <summary>The same limit as <c>Dispute.ResolveNoteMaxLength</c>, which Contracts cannot reference (AD-1).</summary>
    public const int NoteMaxLength = 500;

    private const string NoteInvalid = "A note of 1 to 500 characters after trimming is required.";

    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and the note is free text that
    /// may hold personal data (OWASP Logging Cheat Sheet: data to exclude). Sealed, so nothing overrides it again.
    /// </summary>
    public sealed override string ToString() => nameof(ResolveDisputeRequest);

    /// <summary>The note's length after trimming (AD-4), which no attribute expresses. Runs only once both attributes pass.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Note is not null && Note.Trim().Length > NoteMaxLength)
        {
            yield return new ValidationResult(NoteInvalid, [nameof(Note)]);
        }
    }
}
