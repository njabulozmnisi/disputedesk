using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Json;

namespace DisputeDesk.Contracts.Operations;

/// <summary>
/// The body of <c>POST /api/v1/operations/disputes/{disputeId}/resolve</c> (FR-23). Both fields are checked after the
/// dispute is loaded, so an unknown dispute gets 404 before a missing outcome or a bad note gets 400 (AD-8). Any other
/// property is ignored.
/// </summary>
/// <param name="Outcome">
/// Required: <c>upheld</c> or <c>rejected</c>, exactly; any other spelling, <c>Upheld</c> included, fails to bind.
/// </param>
/// <param name="Note">
/// Required: the note meant for the customer, 1 to 500 characters (UTF-16 code units) after trimming; stored trimmed.
/// </param>
// The endpoint disables automatic validation, so [Required] only documents the contract; the handler and the aggregate
// enforce it after the lookup.
public sealed record ResolveDisputeRequest(
    [property: Required, JsonConverter(typeof(CamelCaseNameOnlyEnumConverter<Outcome>))] Outcome? Outcome,
    [property: Required] string? Note)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and the note is free text that
    /// may hold personal data (OWASP Logging Cheat Sheet: data to exclude). Sealed, so nothing overrides it again.
    /// </summary>
    public sealed override string ToString() => nameof(ResolveDisputeRequest);
}
