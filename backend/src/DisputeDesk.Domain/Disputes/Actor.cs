namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// Whoever performed an action (AD-14a). <see cref="Id"/> is the login's <c>sub</c> for a customer, the token's
/// <c>sub</c> for operations and <c>worker:sla-sweep</c> for automation. It is recorded, never shown to customers.
/// </summary>
public sealed record Actor(ActorType Type, string Id);
