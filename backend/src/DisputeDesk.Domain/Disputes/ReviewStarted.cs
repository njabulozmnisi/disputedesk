namespace DisputeDesk.Domain.Disputes;

/// <summary>Operations started reviewing the dispute (R20).</summary>
public sealed record ReviewStarted(DisputeStatus FromStatus, DisputeStatus ToStatus) : StoredEvent;
