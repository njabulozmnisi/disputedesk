namespace DisputeDesk.Domain.Disputes;

/// <summary>Operations resolved the dispute with an outcome and a note meant for the customer (R20).</summary>
public sealed record DisputeResolved(DisputeStatus FromStatus, DisputeStatus ToStatus, Outcome Outcome, string Note) : StoredEvent;
