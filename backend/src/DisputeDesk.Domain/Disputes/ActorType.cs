namespace DisputeDesk.Domain.Disputes;

/// <summary>Who performed an action (glossary: Actor). Recorded, never used to decide.</summary>
public enum ActorType
{
    Customer,
    Operations,
    Automation,
}
