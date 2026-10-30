namespace DisputeDesk.Infrastructure.Linking;

/// <summary>The <c>Linking</c> configuration section (AD-26).</summary>
public sealed class LinkingOptions
{
    public const string SectionName = "Linking";

    /// <summary>RFC 2104 §3: an HMAC key no shorter than the hash output, 32 bytes for SHA-256.</summary>
    public const int MinimumKeyBytes = 32;

    public const int MinimumQueueCapacity = 1;

    public const int MaximumQueueCapacity = 1000;

    public static readonly TimeSpan MinimumBudget = TimeSpan.FromSeconds(1);

    /// <summary>Npgsql's default command timeout is 30 seconds; a longer budget would let that timeout end a job's lock wait first.</summary>
    public static readonly TimeSpan MaximumBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The secret key of the OTP HMAC, base64 for at least <see cref="MinimumKeyBytes"/> bytes. Never in
    /// appsettings.json and never logged; Compose sets a dev-only value, AWS a SecureString (AD-26).
    /// </summary>
    public string OtpHmacKey { get; set; } = "";

    /// <summary>
    /// How many started jobs may wait for the background job (AD-12), <see cref="MinimumQueueCapacity"/> to
    /// <see cref="MaximumQueueCapacity"/>.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// How long a job may take before it claims the OTP send (AD-12), <see cref="MinimumBudget"/> to
    /// <see cref="MaximumBudget"/>. Configurable so tests can shorten it.
    /// </summary>
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
