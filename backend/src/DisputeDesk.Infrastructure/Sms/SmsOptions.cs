namespace DisputeDesk.Infrastructure.Sms;

/// <summary>The <c>Sms</c> configuration section: the SMTP server that receives every SMS (Mailpit locally).</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    /// <summary>On unless configured off. Local Mailpit accepts plain SMTP only, so Compose turns it off.</summary>
    public bool UseTls { get; set; } = true;
}
