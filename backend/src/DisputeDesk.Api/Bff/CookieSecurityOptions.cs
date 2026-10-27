namespace DisputeDesk.Api.Bff;

public sealed class CookieSecurityOptions
{
    /// <summary><c>Always</c> behind HTTPS in AWS; <c>SameAsRequest</c> on the local http stack only.</summary>
    public CookieSecurePolicy SecurePolicy { get; set; } = CookieSecurePolicy.Always;
}
