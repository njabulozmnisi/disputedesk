namespace DisputeDesk.Domain.Disputes;

// Glossary: Actor. Recorded, never used to decide. The summary below is the API document's text (openapi.json).
/// <summary>Who acted, as events and operations see it (AD-19): <c>customer</c>, <c>operations</c> or <c>automation</c>.</summary>
public enum ActorType
{
    Customer,
    Operations,
    Automation,
}
