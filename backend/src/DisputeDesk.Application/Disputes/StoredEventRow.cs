using System.Text.Json;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Disputes;

/// <summary>
/// One row of <c>StoredEvents</c> (ad-009, AD-22): a <see cref="StoredEvent"/> as it is stored. The shared fields are
/// columns; <see cref="Data"/> is the record's own fields as JSON, with the shared JSON options, and
/// <see cref="EventType"/> is the record's type name. Inserted only by the commit (AD-14), never updated or deleted.
/// </summary>
public sealed class StoredEventRow
{
    private readonly Dispute? _dispute;

    /// <summary>The stored event ID, which is also its outbox row ID and CloudEvent <c>id</c> (ad-009).</summary>
    public required Guid Id { get; init; }

    public required Guid DisputeId { get; init; }

    /// <summary>
    /// Lets the ownership filter reach the dispute's customer (AD-11). A throwing getter over a nullable backing field
    /// is EF Core's documented way to declare a required navigation without the null-forgiving operator.
    /// </summary>
    public Dispute Dispute
    {
        get => _dispute ?? throw new InvalidOperationException("The stored event's dispute is not loaded.");
        private init => _dispute = value;
    }

    public required int Version { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required ActorType ActorType { get; init; }

    public required string ActorId { get; init; }

    public string? TraceId { get; init; }

    /// <summary>Private: on <see cref="DisputeRaised"/> it holds the customer ID, account reference and snapshot.</summary>
    public required string Data { get; init; }

    /// <summary>
    /// The row for <paramref name="storedEvent"/> of <paramref name="dispute"/> under <paramref name="id"/>, recorded in
    /// the trace <paramref name="traceId"/>.
    /// </summary>
    public static StoredEventRow From(Guid id, Dispute dispute, StoredEvent storedEvent, string? traceId) => new()
    {
        Id = id,
        DisputeId = storedEvent.DisputeId,
        Dispute = dispute,
        Version = storedEvent.Version,
        EventType = storedEvent.GetType().Name,
        OccurredAtUtc = storedEvent.OccurredAtUtc,
        ActorType = storedEvent.ActorType,
        ActorId = storedEvent.ActorId,
        TraceId = traceId,
        Data = JsonSerializer.Serialize(storedEvent, storedEvent.GetType(), DisputeDeskJson.Options),
    };

    /// <summary>The stored event this row holds. A row whose <see cref="EventType"/> names no stored event type throws.</summary>
    public StoredEvent ToStoredEvent()
    {
        StoredEvent storedEvent = (StoredEvent?)JsonSerializer.Deserialize(Data, TypeNamed(EventType), DisputeDeskJson.Options)
            ?? throw new InvalidOperationException($"Stored event {Id} has no data.");
        return storedEvent with
        {
            DisputeId = DisputeId,
            Version = Version,
            OccurredAtUtc = OccurredAtUtc,
            ActorType = ActorType,
            ActorId = ActorId,
            TraceId = TraceId,
        };
    }

    private static Type TypeNamed(string eventType) => eventType switch
    {
        nameof(DisputeRaised) => typeof(DisputeRaised),
        nameof(ReviewStarted) => typeof(ReviewStarted),
        nameof(DisputeEscalated) => typeof(DisputeEscalated),
        nameof(DisputeResolved) => typeof(DisputeResolved),
        nameof(DisputeWithdrawn) => typeof(DisputeWithdrawn),
        nameof(SlaWarningRaised) => typeof(SlaWarningRaised),
        _ => throw new InvalidOperationException($"No stored event type is named {eventType}."),
    };
}
