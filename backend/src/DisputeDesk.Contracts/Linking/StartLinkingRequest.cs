using System.ComponentModel.DataAnnotations;

namespace DisputeDesk.Contracts.Linking;

/// <summary>
/// The body of <c>POST /api/v1/linking/start</c> (FR-4). Built-in minimal API validation checks its format; neither
/// value is trimmed or normalised, and any other property, such as a cellphone number, is ignored. A message never
/// repeats the value (AD-3).
/// </summary>
/// <param name="IdNumber">The South African ID number: exactly 13 ASCII digits, the last being the Luhn check digit of the first 12.</param>
/// <param name="AccountNumber">One of the customer's bank account numbers: 6 to 16 ASCII digits.</param>
// The attributes target the properties (`property:`): the OpenAPI generator reads the bounds only from there.
// RegularExpressionAttribute must match the whole value, and passes an empty string, which Required refuses.
public sealed record StartLinkingRequest(
    [property: Required, RegularExpression(StartLinkingRequest.IdNumberPattern, ErrorMessage = "The ID number must be 13 digits.")] string IdNumber,
    [property: Required, RegularExpression(StartLinkingRequest.AccountNumberPattern, ErrorMessage = "The account number must be 6 to 16 digits.")] string AccountNumber)
    : IValidatableObject
{
    /// <summary>[0-9], not \d, which also matches other scripts' digits.</summary>
    public const string IdNumberPattern = "^[0-9]{13}$";

    public const string AccountNumberPattern = "^[0-9]{6,16}$";

    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the ID number
    /// (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public override string ToString() => nameof(StartLinkingRequest);

    /// <summary>The ID number's check digit, which no attribute expresses. Checked only once the 13 digits are there.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        bool hasThirteenDigits = IdNumber is { Length: 13 } && IdNumber.All(char.IsAsciiDigit);
        if (hasThirteenDigits && !HasLuhnCheckDigit(IdNumber))
        {
            yield return new ValidationResult("The ID number's check digit is wrong.", [nameof(IdNumber)]);
        }
    }

    /// <summary>
    /// The Luhn algorithm (ISO/IEC 7812-1, Annex B), which the South African ID number uses for its last digit: from the
    /// right, every second digit is doubled (less 9 when over 9), and the sum of all digits is a multiple of 10.
    /// </summary>
    private static bool HasLuhnCheckDigit(string digits)
    {
        int sum = 0;
        for (int fromRight = 0; fromRight < digits.Length; fromRight++)
        {
            int digit = digits[digits.Length - 1 - fromRight] - '0';
            if (fromRight % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }
}
