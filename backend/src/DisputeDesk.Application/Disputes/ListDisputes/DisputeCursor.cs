using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace DisputeDesk.Application.Disputes.ListDisputes;

/// <summary>
/// A position in the history list (AD-5): the <c>CreatedAtUtc</c> and <c>Id</c> of the last dispute a page returned.
/// On the wire it is opaque, the base64url (RFC 4648 §5) of the UTF-8 text <c>&lt;instant in "O"&gt;|&lt;ID in "D"&gt;</c>.
/// It is a position, not a capability: the ownership filter decides whose disputes follow it (AD-11), so it carries no
/// signature.
/// </summary>
public sealed record DisputeCursor(DateTimeOffset CreatedAtUtc, Guid Id)
{
    /// <summary>A real cursor is 94 characters; built-in validation refuses a longer one than this at the edge.</summary>
    public const int MaxLength = 128;

    public static string Encode(DateTimeOffset createdAtUtc, Guid id) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{createdAtUtc.ToString("O", CultureInfo.InvariantCulture)}|{id:D}"));

    /// <summary>
    /// The position <paramref name="cursor"/> names, or null for any cursor not in the layout: not base64url, no
    /// <c>|</c>, an instant that is not "O" or not at offset zero, or an ID that is not "D" (content after the ID
    /// included). One null for every failure, so the answer reveals no parse step (ASVS 5.0 V16.5.1).
    /// </summary>
    public static DisputeCursor? Decode(string cursor)
    {
        if (!Base64Url.IsValid(cursor))
        {
            return null;
        }

        string text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        int separator = text.IndexOf('|', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        // A non-zero offset is refused, not converted: Npgsql writes a timestamptz parameter only at offset zero.
        bool isInstant = DateTimeOffset.TryParseExact(
            text[..separator], "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset createdAtUtc);
        if (!isInstant || createdAtUtc.Offset != TimeSpan.Zero)
        {
            return null;
        }

        return Guid.TryParseExact(text[(separator + 1)..], "D", out Guid id) ? new DisputeCursor(createdAtUtc, id) : null;
    }
}
