namespace DisputeDesk.Contracts.Disputes;

/// <summary>One timeline entry as the customer sees it: the action, the status it led to, who acted, when, and any note meant for the customer.</summary>
public sealed record TimelineEntryResponse(
    DisputeAction Action,
    CustomerStatus CustomerStatus,
    TimelineActor Actor,
    DateTimeOffset OccurredAt,
    string? Note)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and the note is free text that
    /// may hold personal data (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(TimelineEntryResponse);
}
