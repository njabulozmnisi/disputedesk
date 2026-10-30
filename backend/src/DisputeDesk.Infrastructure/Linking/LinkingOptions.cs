namespace DisputeDesk.Infrastructure.Linking;

/// <summary>The <c>Linking</c> configuration section (AD-26).</summary>
public sealed class LinkingOptions
{
    public const string SectionName = "Linking";

    /// <summary>RFC 2104 §3: an HMAC key no shorter than the hash output, 32 bytes for SHA-256.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// The secret key of the OTP HMAC, base64 for at least <see cref="MinimumKeyBytes"/> bytes. Never in
    /// appsettings.json and never logged; Compose sets a dev-only value, AWS a SecureString (AD-26).
    /// </summary>
    public string OtpHmacKey { get; set; } = "";

    /// <summary>How many started jobs may wait for the background job (AD-12).</summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>How long a job may take before it claims the OTP send (AD-12). Configurable so tests can shorten it.</summary>
    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The decoded <see cref="OtpHmacKey"/>.</summary>
    public byte[] OtpHmacKeyBytes() => Convert.FromBase64String(OtpHmacKey);

    /// <summary>True when <paramref name="key"/> is base64 for at least <see cref="MinimumKeyBytes"/> bytes.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        Span<byte> decoded = new byte[key.Length];
        return Convert.TryFromBase64String(key, decoded, out int length) && length >= MinimumKeyBytes;
    }
}
