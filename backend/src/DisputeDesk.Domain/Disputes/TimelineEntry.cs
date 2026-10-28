namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// One entry in a dispute's timeline (glossary: Timeline): the status an action moved it to, who acted and any note
/// meant for the customer. Created only by <see cref="Dispute"/>.
/// </summary>
public sealed class TimelineEntry
{
    // The navigation lets the ownership filter reach the dispute's customer (AD-11). A throwing getter over a nullable
    // backing field is EF Core's documented way to declare a required navigation without the null-forgiving operator.
    private readonly Dispute? _dispute;

    internal TimelineEntry(Dispute dispute, DisputeChange change, string? visibleNote)
    {
        Id = Guid.CreateVersion7(change.OccurredAt);
        DisputeId = dispute.Id;
        _dispute = dispute;
        FromStatus = change.FromStatus;
        ToStatus = change.ToStatus;
        Action = change.Action;
        ActorType = change.Actor.Type;
        ActorId = change.Actor.Id;
        VisibleNote = visibleNote;
        OccurredAtUtc = change.OccurredAt;
    }

    // EF Core materialises entries through this constructor and then sets every property from the row.
    private TimelineEntry()
    {
        ActorId = "";
    }

    public Guid Id { get; private set; }

    public Guid DisputeId { get; private set; }

    public Dispute Dispute => _dispute ?? throw new InvalidOperationException("The timeline entry's dispute is not loaded.");

    /// <summary>Null on the entry that raised the dispute.</summary>
    public DisputeStatus? FromStatus { get; private set; }

    public DisputeStatus ToStatus { get; private set; }

    public DisputeAction Action { get; private set; }

    public ActorType ActorType { get; private set; }

    /// <summary>Never shown to customers (AD-13).</summary>
    public string ActorId { get; private set; }

    /// <summary>The note meant for the customer, or null.</summary>
    public string? VisibleNote { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
