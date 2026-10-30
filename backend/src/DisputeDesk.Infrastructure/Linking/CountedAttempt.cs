namespace DisputeDesk.Infrastructure.Linking;

/// <summary>
/// The login's current challenge as the attempt statement left it (<see cref="OtpAttempts.CountAttemptAsync"/>): an
/// unmapped type that EF Core fills from the statement's columns by name (EF Core, SQL queries: querying unmapped types).
/// </summary>
public sealed class CountedAttempt
{
    public Guid Id { get; init; }

    /// <summary>Null on a blank challenge.</summary>
    public string? CustomerId { get; init; }

    /// <summary>Null on a blank or released challenge.</summary>
    public byte[]? OtpHmac { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>The attempts so far, this one included.</summary>
    public int Attempts { get; init; }
}
