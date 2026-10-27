using System.Buffers.Text;
using System.Text;

namespace DisputeDesk.Application.Transactions;

/// <summary>
/// The opaque cursor clients get (AD-5): core banking's own cursor, base64url encoded, so no client builds or reads one.
/// </summary>
public static class TransactionCursor
{
    /// <summary>A real cursor is about 15 characters; anything longer than this never came from the Api.</summary>
    public const int MaxLength = 200;

    public static string Encode(string coreBankingCursor) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(coreBankingCursor));

    /// <summary>
    /// Core banking's cursor, or null when <paramref name="cursor"/> is longer than <see cref="MaxLength"/>, is not
    /// base64url or decodes to nothing.
    /// </summary>
    public static string? Decode(string cursor)
    {
        if (cursor.Length > MaxLength || !Base64Url.IsValid(cursor))
        {
            return null;
        }

        string coreBankingCursor = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        return coreBankingCursor.Length > 0 ? coreBankingCursor : null;
    }
}
