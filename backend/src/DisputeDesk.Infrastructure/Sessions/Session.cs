namespace DisputeDesk.Infrastructure.Sessions;

/// <summary>
/// A signed-in login's server-side session (AD-7). The browser cookie holds only the session key; this row holds
/// the key's SHA-256 and the data-protected authentication ticket.
/// </summary>
public sealed class Session
{
    public Guid Id { get; set; }

    public required byte[] KeyHash { get; set; }

    /// <summary>The login's <c>sub</c> claim.</summary>
    public required string LoginSub { get; set; }

    public required byte[] Ticket { get; set; }

    /// <summary>The idle limit: moved forward on every authenticated request.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>The absolute limit, fixed when the session starts.</summary>
    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }
}
