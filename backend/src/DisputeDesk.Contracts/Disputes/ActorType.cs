namespace DisputeDesk.Contracts.Disputes;

/// <summary>Who acted, as events and operations see it (AD-19): <c>customer</c>, <c>operations</c> or <c>automation</c>.</summary>
public enum ActorType
{
    Customer,
    Operations,
    Automation,
}
