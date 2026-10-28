namespace DisputeDesk.Contracts.Disputes;

/// <summary>One timeline entry as the customer sees it: the action, the status it led to, who acted, when, and any note meant for the customer.</summary>
public sealed record TimelineEntryResponse(
    DisputeAction Action,
    CustomerStatus CustomerStatus,
    TimelineActor Actor,
    DateTimeOffset OccurredAt,
    string? Note);
