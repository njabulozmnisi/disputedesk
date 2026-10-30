using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DisputeDesk.Application.Linking;

/// <summary>The OTP of a linking challenge (glossary: OTP): six random digits, stored only as an HMAC.</summary>
public static class Otp
{
    /// <summary>32 zero bytes: the length of an HMAC-SHA256, which no real HMAC equals in practice.</summary>
    private static readonly byte[] NoOtpHmac = new byte[32];

    /// <summary>
    /// A new OTP from the cryptographic random number generator, written as six digits with leading zeros (NIST SP
    /// 800-63B §3.1.3.2: at least 6 digits from an approved random bit generator).
    /// </summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>
    /// HMAC-SHA256 (RFC 2104) under <paramref name="key"/> over the challenge ID followed by the code, both as UTF-8 text,
    /// so the same code never hashes alike on two challenges. Story 6.2 computes the same input to verify.
    /// </summary>
    public static byte[] Hmac(byte[] key, Guid challengeId, string code) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(challengeId.ToString("D") + code));

    /// <summary>
    /// True when <paramref name="code"/> is the challenge's OTP. The HMAC is always computed and compared in constant
    /// time (<see cref="CryptographicOperations.FixedTimeEquals"/>), against a fixed dummy when no OTP was sent, so a
    /// blank or released challenge costs the same work as a real one.
    /// </summary>
    public static bool Matches(byte[] key, Guid challengeId, string code, byte[]? otpHmac) =>
        CryptographicOperations.FixedTimeEquals(Hmac(key, challengeId, code), otpHmac ?? NoOtpHmac);
}
