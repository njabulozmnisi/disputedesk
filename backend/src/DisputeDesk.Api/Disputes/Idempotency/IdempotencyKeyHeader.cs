using System.Text;
using DisputeDesk.Application.Disputes.RaiseDispute;
using Microsoft.Extensions.Primitives;

namespace DisputeDesk.Api.Disputes.Idempotency;

/// <summary>
/// The <c>Idempotency-Key</c> request header. draft-ietf-httpapi-idempotency-key-header defines it as an RFC 9651
/// Structured Field Item whose value is a String, so it is quoted: <c>Idempotency-Key: "&lt;uuid&gt;"</c>.
/// </summary>
public static class IdempotencyKeyHeader
{
    public const string Name = "Idempotency-Key";

    /// <summary>
    /// The decoded key, or null when the header is missing, is not exactly one String (RFC 9651 §4.2 and §4.2.5), or
    /// decodes to an empty string or one longer than <see cref="IdempotencyKey.KeyMaxLength"/>. A String holds only
    /// printable ASCII, so that bound needs no check of its own. Several header lines are combined with commas first, as
    /// RFC 9651 §4.2 says, which is never one String. Parameters (<c>"key";a=1</c>) are refused: the draft defines none.
    /// </summary>
    public static string? Parse(StringValues values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        // §4.2: leading spaces are discarded before the item and after it, and anything else left over fails.
        string input = string.Join(',', values.ToArray()).TrimStart(' ');
        if (input.Length == 0 || input[0] != '"')
        {
            return null;
        }

        var key = new StringBuilder();
        int index = 1;
        while (index < input.Length)
        {
            char character = input[index];
            index++;
            if (character == '"')
            {
                bool nothingAfter = input[index..].TrimStart(' ').Length == 0;
                return nothingAfter && key.Length is >= 1 and <= IdempotencyKey.KeyMaxLength ? key.ToString() : null;
            }

            if (character == '\\')
            {
                // §4.2.5: a backslash escapes only a quote or a backslash.
                if (index == input.Length || input[index] is not ('"' or '\\'))
                {
                    return null;
                }

                character = input[index];
                index++;
            }
            else if (character is < ' ' or > '~')
            {
                // §4.2.5: anything outside VCHAR and SP (%x20-7E), control characters and non-ASCII included, fails.
                return null;
            }

            key.Append(character);
        }

        // No closing quote.
        return null;
    }
}
