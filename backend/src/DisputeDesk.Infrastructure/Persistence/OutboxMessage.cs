namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// A dispute event waiting for the relay (AD-17). Inserted only by the commit (AD-14), in the same transaction as the
/// change it describes, and updated only by the relay. <see cref="Payload"/> is the whole structured CloudEvent, the
/// exact bytes the relay produces; the other columns are copied out of it for querying.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>The CloudEvents <c>id</c>.</summary>
    public required Guid Id { get; init; }

    public required string EventType { get; init; }

    /// <summary>The Kafka key; events of one dispute are produced in <see cref="DisputeVersion"/> order.</summary>
    public required Guid DisputeId { get; init; }

    public required int DisputeVersion { get; init; }

    public required string Payload { get; init; }

    /// <summary>The W3C <c>traceparent</c> of the change, or null when no activity was recorded.</summary>
    public string? TraceParent { get; init; }

    public string? TraceState { get; init; }

    public OutboxStatus Status { get; set; }

    /// <summary>Per-record failures so far; the 5th sets <see cref="OutboxStatus.Failed"/>.</summary>
    public int Attempts { get; set; }

    /// <summary>When the relay may next try; the change's instant at first.</summary>
    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public string? LastError { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
