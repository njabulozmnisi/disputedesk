namespace DisputeDesk.Domain.Disputes;

/// <summary>The customer withdrew the dispute, with an optional note (R19).</summary>
public sealed record DisputeWithdrawn(DisputeStatus FromStatus, DisputeStatus ToStatus, string? Note) : StoredEvent;
