using System.Text.Json.Serialization;

namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// A fact in one dispute's own stream (ad-009; glossary: Stored event), appended only by the <see cref="Dispute"/>
/// aggregate. Replaying a stream in <see cref="Version"/> order rebuilds the dispute (AD-13). Each type is a sealed record
/// deriving from this one with its own data. The fields here are <c>StoredEvents</c> columns of their own, so they are
/// left out of the JSON in <c>Data</c>, which holds only the type's own data (AD-22).
/// </summary>
public abstract record StoredEvent
{
    [JsonIgnore]
    public Guid DisputeId { get; init; }

    /// <summary>1 for <see cref="DisputeRaised"/>, then one more for each stored event of the dispute (AD-15).</summary>
    [JsonIgnore]
    public int Version { get; init; }

    [JsonIgnore]
    public DateTimeOffset OccurredAtUtc { get; init; }

    [JsonIgnore]
    public ActorType ActorType { get; init; }

    /// <summary>Never shown to customers (AD-13).</summary>
    [JsonIgnore]
    public string ActorId { get; init; } = "";

    /// <summary>The W3C trace ID of the request or batch that appended it; the commit (AD-14) records it.</summary>
    [JsonIgnore]
    public string? TraceId { get; init; }

    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and a stored event holds personal
    /// data (OWASP Logging Cheat Sheet: data to exclude). Sealed, so no derived record generates its own.
    /// </summary>
    public sealed override string ToString() => GetType().Name;
}
