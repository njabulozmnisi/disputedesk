using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Contracts.Linking;

/// <summary>
/// The body of <c>POST /api/v1/linking/verify</c> (FR-5). Built-in minimal API validation checks its format; the code is
/// not trimmed, and any other property is ignored. No field names a challenge or a customer: verification acts only on
/// the login's current challenge. A message never repeats the value (AD-3).
/// </summary>
/// <param name="Code">The OTP from the SMS: exactly six ASCII digits, leading zeros included.</param>
// The attributes target the property (`property:`): the OpenAPI generator reads the bounds only from there.
// RegularExpressionAttribute must match the whole value, and passes an empty string, which Required refuses.
public sealed record VerifyOtpRequest(
    [property: Required, RegularExpression(VerifyOtpRequest.CodePattern, ErrorMessage = "The code must be 6 digits.")] string Code)
{
    /// <summary>[0-9], not \d, which also matches other scripts' digits.</summary>
    public const string CodePattern = "^[0-9]{6}$";

    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the OTP
    /// (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public override string ToString() => nameof(VerifyOtpRequest);
}
