namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// The dispute is close to its SLA (AD-18). It changes no status, is never published and is never shown to customers.
/// </summary>
public sealed record SlaWarningRaised : StoredEvent;
