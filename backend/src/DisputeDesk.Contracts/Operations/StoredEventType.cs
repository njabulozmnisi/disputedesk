namespace DisputeDesk.Contracts.Operations;

/// <summary>The type of a stored event on an operations timeline: the closed set of AD-14a, camelCase on the wire.</summary>
public enum StoredEventType
{
    DisputeRaised,
    ReviewStarted,
    DisputeEscalated,
    DisputeResolved,
    DisputeWithdrawn,
    SlaWarningRaised,
}
