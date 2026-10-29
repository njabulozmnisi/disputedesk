namespace DisputeDesk.Domain.Disputes;

/// <summary>The dispute was escalated, by operations or by the SLA sweep (R20, R22).</summary>
public sealed record DisputeEscalated(DisputeStatus FromStatus, DisputeStatus ToStatus) : StoredEvent;
