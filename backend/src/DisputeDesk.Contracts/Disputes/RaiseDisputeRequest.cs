using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// The body of <c>POST /api/v1/disputes</c> (FR-13). Built-in minimal API validation checks its shape before the
/// transaction is looked up (AD-8), so each rule here is one a body breaks on its own; rules that need the transaction
/// (disputability, an expected amount below the transaction amount) come after the lookup. Any other property, such as
/// an amount, date or description, is ignored.
/// </summary>
/// <param name="AccountRef">The transaction's <c>accountRef</c> from the transactions list, 1 to 32 characters.</param>
/// <param name="TransactionId">The transaction's <c>transactionId</c>, 1 to 32 characters.</param>
/// <param name="Reason">Why the customer disputes the transaction.</param>
/// <param name="Note">Optional, at most 1,000 characters (UTF-16 code units) after trimming.</param>
/// <param name="ExpectedAmount">
/// The amount the customer expected to pay, in ZAR, as a decimal string such as <c>80.00</c>: required for the reason
/// <c>wrongAmount</c> and rejected for every other reason; at least 0.00, at most 2 decimals, and below the
/// transaction amount.
/// </param>
// The attributes target the properties (`property:`): the OpenAPI generator reads the bounds only from there.
public sealed record RaiseDisputeRequest(
    [property: Required, StringLength(RaiseDisputeRequest.IdMaxLength, MinimumLength = 1)] string AccountRef,
    [property: Required, StringLength(RaiseDisputeRequest.IdMaxLength, MinimumLength = 1)] string TransactionId,
    [property: Required] Reason? Reason,
    string? Note = null,
    // MinLength because RegularExpressionAttribute passes an empty string as valid.
    [property: RegularExpression(RaiseDisputeRequest.AmountPattern), MinLength(1)] string? ExpectedAmount = null) : IValidatableObject
{
    /// <summary>The longest ID core banking issues (the client's ID shape, story 2.3).</summary>
    public const int IdMaxLength = 32;

    /// <summary>The same limit as <c>Dispute.NoteMaxLength</c>, which Contracts cannot reference (AD-1).</summary>
    public const int NoteMaxLength = 1000;

    /// <summary>0.00 or more, with at most 2 decimals, within <c>numeric(19,2)</c>.</summary>
    public const string AmountPattern = @"^[0-9]{1,17}(\.[0-9]{1,2})?$";

    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and the note is free text that
    /// may hold personal data (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public override string ToString() => nameof(RaiseDisputeRequest);

    /// <summary>The rules between fields, and the note's length after trimming (AD-4), which no attribute expresses.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Note is not null && Note.Trim().Length > NoteMaxLength)
        {
            yield return new ValidationResult("The note must be at most 1000 characters after trimming.", [nameof(Note)]);
        }

        bool isWrongAmount = Reason == Disputes.Reason.WrongAmount;
        if (isWrongAmount && ExpectedAmount is null)
        {
            yield return new ValidationResult("An expected amount is required for the reason wrong amount.", [nameof(ExpectedAmount)]);
        }

        if (!isWrongAmount && ExpectedAmount is not null)
        {
            yield return new ValidationResult("An expected amount is allowed only for the reason wrong amount.", [nameof(ExpectedAmount)]);
        }
    }
}
